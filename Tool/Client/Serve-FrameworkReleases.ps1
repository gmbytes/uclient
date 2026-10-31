#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseRoot,[Parameter(Mandatory)][string]$ControlRoot,
    [Parameter(Mandatory)][string]$ListenAddress,[Parameter(Mandatory)][ValidateRange(1024,65535)][int]$Port,
    [ValidateRange(0,200)][int]$ChunkDelayMilliseconds=0,[string]$OwnerReceipt)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Framework-Evidence.ps1"
. "$PSScriptRoot/Framework-IO.ps1"
. "$PSScriptRoot/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-TransferPolicy.ps1"
Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($OwnerReceipt)) 'E_PREPARATION_GATE' 'An explicit owned qualification handoff is required.'
$receipt=Read-FrameworkJson $OwnerReceipt
Assert-FrameworkFields $receipt @('schemaVersion','scope','ownerProcess','platform','runId','deviceId','playerArchive','buildResult','runtimePlayerRoot','releaseRoot','controlRoot','endpoint','script','scriptSha256')
Require-FrameworkFact ($receipt.schemaVersion -eq 1 -and $receipt.scope -ceq 'QualificationOnly' -and
    [IO.Path]::GetFullPath($OwnerReceipt) -ceq [IO.Path]::GetFullPath((Join-Path $ControlRoot 'server-owner.json')) -and
    [IO.Path]::GetFullPath($ReleaseRoot) -ceq $receipt.releaseRoot -and [IO.Path]::GetFullPath($ControlRoot) -ceq $receipt.controlRoot -and
    ('http://'+$ListenAddress+':'+$Port+'/') -ceq $receipt.endpoint -and [IO.Path]::GetFullPath($PSCommandPath) -ceq $receipt.script -and
    (Get-FrameworkFileHash $PSCommandPath) -ceq $receipt.scriptSha256) 'E_SERVER_HANDOFF' 'Server arguments or script differ from the owned qualification handoff.'
$ownerProcess=[Diagnostics.Process]::GetProcessById($receipt.ownerProcess.processId)
$observedOwner=$receipt.ownerProcess.Clone();$observedOwner.processId=$ownerProcess.Id;$observedOwner.startedUtc=$ownerProcess.StartTime.ToUniversalTime().ToString('o');$observedOwner.executable=$ownerProcess.MainModule.FileName;$observedOwner.executableSha256=Get-FrameworkFileHash $observedOwner.executable
Assert-FrameworkOwnedProcess $receipt.ownerProcess $observedOwner
$serve={
param($qualification)
Assert-FrameworkEffectsReleased
$address=[Net.IPAddress]::Parse($ListenAddress)
Require-FrameworkFact ([Net.IPAddress]::IsLoopback($address)) 'E_SERVER_ADDRESS' 'D2 update service must bind explicit loopback; Android uses an owned reverse.'
$ReleaseRoot=[IO.Path]::GetFullPath($ReleaseRoot);$ControlRoot=[IO.Path]::GetFullPath($ControlRoot)
Assert-FrameworkNoReparse $ReleaseRoot;Assert-FrameworkNoReparse $ControlRoot
Require-FrameworkFact ((Test-Path -LiteralPath $ControlRoot -PathType Container) -and -not(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-ready'))) 'E_SERVER_OWNER' 'A fresh explicit owned server control directory is required.'
$listener=[Net.HttpListener]::new();$listener.Prefixes.Add("http://$($address.ToString()):$Port/");$listener.Start()
$readyPath=Join-Path $ControlRoot 'server-ready.tmp'
$ready=[IO.FileStream]::new($readyPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.Encoding]::UTF8.GetBytes([string]$PID);$ready.Write($bytes,0,$bytes.Length);$ready.Flush($true)}finally{$ready.Dispose()}
# 就绪标记在写句柄关闭后才发布，父进程不会读到尚在写入的文件。
[IO.File]::Move($readyPath,(Join-Path $ControlRoot 'server-ready'))
try{
    $pending=$listener.GetContextAsync()
    while(-not(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-stop'))){
        $ownerProcess.Refresh();Require-FrameworkFact (-not $ownerProcess.HasExited -and $ownerProcess.StartTime.ToUniversalTime().ToString('o') -ceq $receipt.ownerProcess.startedUtc) 'E_SERVER_OWNER' 'Original server owner is no longer live.'
        if(-not $pending.Wait(100)){continue}
        $request=$pending.Result;$status=404;$bytesSent=0L;$active=$null;$relative=$null;$started=[DateTime]::UtcNow.ToString('o');$transfer=$null;$errorText=$null
        try{
            $active=[IO.File]::ReadAllText((Join-Path $ControlRoot 'active-release.txt')).Trim()
            Require-FrameworkFact (-not[IO.Path]::IsPathRooted($active) -and $active -cnotmatch '(^|[/\\])\.\.([/\\]|$)') 'E_SERVER_SELECTION' 'Unsafe selected release path.'
            $base=[IO.Path]::GetFullPath((Join-Path $ReleaseRoot $active))
            Require-FrameworkFact ($base.StartsWith($ReleaseRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_SERVER_SELECTION' 'Selected release escapes its owned root.'
            $relative=[Uri]::UnescapeDataString($request.Request.Url.AbsolutePath).TrimStart('/')
            Require-FrameworkFact ($request.Request.HttpMethod -ceq 'GET' -and $relative -cnotmatch '[\\:]|(^|/)\.\.?(/|$)') 'E_SERVER_PATH' 'Unsupported request or unsafe release path.'
            $file=Get-FrameworkPublicPayloadPath $base $relative
            $policyPath=Join-Path $ControlRoot 'transfer-policy.json';$policy=$null
            if(Test-Path -LiteralPath $policyPath -PathType Leaf){$policy=Read-FrameworkJson $policyPath;Assert-FrameworkTransferPolicy $policy $base}
            if(Test-Path -LiteralPath $file -PathType Leaf){
                $sourceStream=[IO.File]::OpenRead($file)
                try{
                    $request.Response.ContentLength64=$sourceStream.Length;$buffer=[byte[]]::new(16384)
                    if($null -ne $policy -and $relative -ceq $policy.relativePath){
                        $hasher=[Security.Cryptography.SHA256]::Create();try{$actualHash=([BitConverter]::ToString($hasher.ComputeHash($sourceStream))).Replace('-','').ToLowerInvariant()}finally{$hasher.Dispose()}
                        Require-FrameworkFact ($actualHash -ceq $policy.sha256 -and $sourceStream.Length -eq $policy.expectedBytes) 'E_TRANSFER_FILE' 'Opened/locked response bytes differ from authenticated policy.'
                        $sourceStream.Position=0;$remaining=[long]$policy.holdBytes
                        while($remaining -gt 0){$count=$sourceStream.Read($buffer,0,[int][Math]::Min($remaining,$buffer.Length));Require-FrameworkFact ($count -gt 0) 'E_TRANSFER_PREFIX' 'Source ended before held prefix.';$request.Response.OutputStream.Write($buffer,0,$count);$request.Response.OutputStream.Flush();$bytesSent+=$count;$remaining-=$count}
                        $transfer=@{runId=$policy.runId;caseId=$policy.caseId;transferId=[Guid]::NewGuid().ToString('N');serverProcessId=$PID;releaseId=$policy.releaseId;manifestSha256=$policy.manifestSha256;relativePath=$relative;sha256=$actualHash;expectedBytes=$sourceStream.Length;contentLength=$request.Response.ContentLength64;bytesSent=$bytesSent;phase='Holding';utc=[DateTime]::UtcNow.ToString('o');completed=$false}
                        $directory=Join-Path $ControlRoot ('transfers/'+$transfer.transferId);[IO.Directory]::CreateDirectory($directory)|Out-Null
                        Write-FrameworkJsonNew (Join-Path $directory 'holding.json') $transfer
                        $clock=[Diagnostics.Stopwatch]::StartNew();$control=$null;$action='Hold'
                        while($action -ceq 'Hold'){
                            $ownerProcess.Refresh();Require-FrameworkFact (-not $ownerProcess.HasExited) 'E_SERVER_OWNER' 'Original transfer owner is no longer live.'
                            $abortPath=Join-Path $directory 'abort.json'
                            if(Test-Path -LiteralPath $abortPath -PathType Leaf){try{$control=Read-FrameworkJson $abortPath}catch [IO.IOException]{Start-Sleep -Milliseconds 20;continue}}
                            $action=Get-FrameworkTransferAction $policy $transfer $control $clock.Elapsed.TotalSeconds
                            if(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-stop')){$action='ServerStopped'}
                            if($action -ceq 'Hold'){Start-Sleep -Milliseconds 20}
                        }
                        $terminal=$transfer.Clone();$terminal.phase=$action;$terminal.utc=[DateTime]::UtcNow.ToString('o');$terminal.reason=if($action -ceq 'Abort'){$control.reason}else{$action};$terminal.nativeProcessId=if($control){$control.nativeProcessId}else{$null}
                        # Close() 不保证立刻终止未满足 Content-Length 的连接；显式中止后才发布完成记录。
                        $request.Response.Abort()
                        Write-FrameworkJsonNew (Join-Path $directory 'terminal.json') $terminal
                        # Deliberately send no remaining body bytes. Aborting/timeout always leaves Content-Length incomplete.
                        $status=200
                    }else{
                        while(($count=$sourceStream.Read($buffer,0,$buffer.Length)) -gt 0){$request.Response.OutputStream.Write($buffer,0,$count);$request.Response.OutputStream.Flush();$bytesSent+=$count;if($ChunkDelayMilliseconds){Start-Sleep -Milliseconds $ChunkDelayMilliseconds}}
                        $status=200
                    }
                }finally{$sourceStream.Dispose()}
            }else{$request.Response.StatusCode=404}
        }catch{$errorText=$_.Exception.Message;$status=500;try{$request.Response.StatusCode=500}catch{}}
        finally{
            try{$request.Response.Close()}catch{}
            $record=@{utc=$started;finishedUtc=[DateTime]::UtcNow.ToString('o');path=$relative;active=$active;status=$status;bytes=$bytesSent;serverProcessId=$PID;transferId=if($transfer){$transfer.transferId}else{$null};error=$errorText}|ConvertTo-Json -Compress
            [IO.File]::AppendAllText((Join-Path $ControlRoot 'server-requests.jsonl'),$record+"`n",[Text.UTF8Encoding]::new($false))
        }
        $pending=$listener.GetContextAsync()
    }
}finally{$listener.Stop();$listener.Close()}
}
# 子进程重新验证原归档，并独立关闭自己的资格作用域；不接受环境变量开启执行。
Invoke-FrameworkNativeQualification -Platform $receipt.platform -RunId $receipt.runId -DeviceId $receipt.deviceId -PlayerArchive $receipt.playerArchive -BuildResult $receipt.buildResult -RuntimePlayerRoot $receipt.runtimePlayerRoot -OutputDirectory (Join-Path $ControlRoot 'worker-qualification') -Body $serve | Out-Null
