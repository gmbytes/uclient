#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseRoot,[Parameter(Mandatory)][string]$ControlRoot,
    [Parameter(Mandatory)][string]$ListenAddress,[Parameter(Mandatory)][ValidateRange(1024,65535)][int]$Port,
    [ValidateRange(0,200)][int]$ChunkDelayMilliseconds=0)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Framework-Evidence.ps1"
. "$PSScriptRoot/Framework-IO.ps1"
. "$PSScriptRoot/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-TransferPolicy.ps1"
Assert-FrameworkEffectsReleased
$address=[Net.IPAddress]::Parse($ListenAddress)
Require-FrameworkFact ([Net.IPAddress]::IsLoopback($address)) 'E_SERVER_ADDRESS' 'D2 update service must bind explicit loopback; Android uses an owned reverse.'
$ReleaseRoot=[IO.Path]::GetFullPath($ReleaseRoot);$ControlRoot=[IO.Path]::GetFullPath($ControlRoot)
Assert-FrameworkNoReparse $ReleaseRoot;Assert-FrameworkNoReparse $ControlRoot
Require-FrameworkFact ((Test-Path -LiteralPath $ControlRoot -PathType Container) -and -not(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-ready'))) 'E_SERVER_OWNER' 'A fresh explicit owned server control directory is required.'
$listener=[Net.HttpListener]::new();$listener.Prefixes.Add("http://$($address.ToString()):$Port/");$listener.Start()
$ready=[IO.FileStream]::new((Join-Path $ControlRoot 'server-ready'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$bytes=[Text.Encoding]::UTF8.GetBytes([string]$PID);$ready.Write($bytes,0,$bytes.Length);$ready.Flush($true)}finally{$ready.Dispose()}
try{
    $pending=$listener.GetContextAsync()
    while(-not(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-stop'))){
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
                            $abortPath=Join-Path $directory 'abort.json'
                            if(Test-Path -LiteralPath $abortPath -PathType Leaf){try{$control=Read-FrameworkJson $abortPath}catch [IO.IOException]{Start-Sleep -Milliseconds 20;continue}}
                            $action=Get-FrameworkTransferAction $policy $transfer $control $clock.Elapsed.TotalSeconds
                            if(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-stop')){$action='ServerStopped'}
                            if($action -ceq 'Hold'){Start-Sleep -Milliseconds 20}
                        }
                        $terminal=$transfer.Clone();$terminal.phase=$action;$terminal.utc=[DateTime]::UtcNow.ToString('o');$terminal.reason=if($action -ceq 'Abort'){$control.reason}else{$action};$terminal.nativeProcessId=if($control){$control.nativeProcessId}else{$null}
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
