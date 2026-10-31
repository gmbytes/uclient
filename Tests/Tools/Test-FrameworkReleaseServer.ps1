#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/ReleaseServer-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$f=New-FrameworkFileBundle (Join-Path $root 'fixture')
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_SERVER_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_SERVER_FAIL $Name $($_.Exception.Message)"}}
Check 'owned qualification server reaches readiness and stops through its control file' {
    $result=Invoke-FrameworkNativeQualification -Platform Windows64 -RunId serverqual -PlayerArchive $f.archive -BuildResult $f.result -RuntimePlayerRoot (Join-Path $f.archive 'Player') -OutputDirectory (Join-Path $root 'qualification') -Body {
        param($context)
        $context.updateEndpoint='http://127.0.0.1:'+ $port + '/';$context.releaseRoot=$root;$context.chunkDelayMilliseconds=0
        $exe=(Get-Command pwsh).Source;$script=Join-Path $project 'Tool/Client/Serve-FrameworkReleases.ps1'
        $command=@{file=$exe;fileSha256=(Get-FrameworkFileHash $exe);script=$script;scriptSha256=(Get-FrameworkFileHash $script)}
        $server=$null
        try{
            $server=Start-FrameworkReleaseServer $command $context (Join-Path $root 'server')
            $observed=Get-FrameworkWindowsOwnedObservation $server
            Require ($observed.processId -eq $server.handle.Id -and -not $server.handle.HasExited) 'Server readiness is not bound to its actual process.'
            Stop-FrameworkReleaseServer $server
            Require ($server.handle.HasExited -and $server.handle.ExitCode -eq 0) 'Owned server did not stop cleanly.'
            return @{processId=$observed.processId;terminal=$true}
        }finally{if($server -and -not $server.handle.HasExited){Stop-FrameworkReleaseServer $server}}
    }
    Require ($result.succeeded -and -not $result.nativeAcceptance) 'Server qualification changed acceptance scope.'
}
Check 'localhost native endpoint binds the explicit loopback listener' {
    $published=Join-Path $root 'localhost-input';[IO.Directory]::CreateDirectory((Join-Path $published 'Release'))|Out-Null
    [IO.File]::WriteAllText((Join-Path $published 'Release/channel.json'),'localhost-public-response')
    $result=Invoke-FrameworkNativeQualification -Platform Windows64 -RunId serverqual -PlayerArchive $f.archive -BuildResult $f.result -RuntimePlayerRoot (Join-Path $f.archive 'Player') -OutputDirectory (Join-Path $root 'qualification-localhost') -Body {
        param($context)
        $context.updateEndpoint='http://localhost:'+ $port + '/';$context.releaseRoot=$published;$context.chunkDelayMilliseconds=0
        $exe=(Get-Command pwsh).Source;$script=Join-Path $project 'Tool/Client/Serve-FrameworkReleases.ps1';$server=$null;$client=$null
        try{
            $server=Start-FrameworkReleaseServer @{file=$exe;fileSha256=(Get-FrameworkFileHash $exe);script=$script;scriptSha256=(Get-FrameworkFileHash $script)} $context (Join-Path $root 'server-localhost')
            [IO.File]::WriteAllText((Join-Path $server.outputDirectory 'active-release.txt'),'Release')
            $handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=$false;$client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(5)
            $text=$client.GetStringAsync($context.updateEndpoint+'channel.json').GetAwaiter().GetResult()
            Require ($text -ceq 'localhost-public-response') 'Native localhost URL did not reach the loopback service.'
        }finally{if($client){$client.Dispose()};if($server -and -not $server.handle.HasExited){Stop-FrameworkReleaseServer $server}}
    }
    Require ($result.succeeded -and -not $result.nativeAcceptance) 'Localhost service qualification claimed acceptance.'
}
foreach($scenario in @('missing','scope','endpoint','scriptSha256','ownerStartTime')){
    Check "child server rejects invalid handoff $scenario before listening" {
        $result=Invoke-FrameworkNativeQualification -Platform Windows64 -RunId serverqual -PlayerArchive $f.archive -BuildResult $f.result -RuntimePlayerRoot (Join-Path $f.archive 'Player') -OutputDirectory (Join-Path $root ('qualification-'+$scenario)) -Body {
            param($context)
            $control=Join-Path $root ('rejected-'+$scenario);[IO.Directory]::CreateDirectory($control)|Out-Null
            $recipe=Read-FrameworkJson (Join-Path $root 'server/server-owner.json');$recipe.controlRoot=$control
            if($scenario -ceq 'scope'){$recipe.scope='Acceptance'}
            if($scenario -ceq 'endpoint'){$recipe.endpoint='http://127.0.0.1:1/'}
            if($scenario -ceq 'scriptSha256'){$recipe.scriptSha256='0'*64}
            if($scenario -ceq 'ownerStartTime'){$recipe.ownerProcess.startedUtc='2000-01-01T00:00:00Z'}
            $path=Join-Path $control 'server-owner.json';Write-FrameworkJsonNew $path $recipe
            $args=@('-NoProfile','-File',(Join-Path $project 'Tool/Client/Serve-FrameworkReleases.ps1'),'-ReleaseRoot',$root,'-ControlRoot',$control,'-ListenAddress','127.0.0.1','-Port',[string]$port)
            if($scenario -cne 'missing'){$args+=@('-OwnerReceipt',$path)}
            $exe=(Get-Command pwsh).Source
            $receipt=Invoke-FrameworkReceiptCommand @{file=$exe;sha256=(Get-FrameworkFileHash $exe);arguments=$args} (Join-Path $root ('launch-'+$scenario))
            Require ($receipt.terminal -eq $true -and $receipt.exitCode -ne 0 -and -not(Test-Path (Join-Path $control 'server-ready'))) 'Invalid handoff started an independently listening service.'
        }
        Require ($result.succeeded -and -not $result.nativeAcceptance) 'Rejected service changed qualification scope.'
    }
}
Check 'held response abort terminates the actual client before its deadline' {
    $published=Join-Path $root 'published';[IO.Directory]::CreateDirectory((Join-Path $published 'Release/Hotfix'))|Out-Null
    $payload=Join-Path $published 'Release/Hotfix/Client.Game.dll';[IO.File]::WriteAllBytes($payload,[byte[]](0..99))
    Write-FrameworkJsonNew (Join-Path $published 'Release/release.json') @{releaseId='held';files=@(@{path='Hotfix/Client.Game.dll';length=100;sha256=(Get-FrameworkFileHash $payload)})}
    foreach($name in @('release.sig','channel.json','channel.sig')){[IO.File]::WriteAllText((Join-Path $published ('Release/'+$name)),'synthetic-public-envelope')}
    Write-FrameworkJsonNew (Join-Path $published 'known.json') @{files=@()}
    $result=Invoke-FrameworkNativeQualification -Platform Windows64 -RunId serverqual -PlayerArchive $f.archive -BuildResult $f.result -RuntimePlayerRoot (Join-Path $f.archive 'Player') -OutputDirectory (Join-Path $root 'qualification-hold') -Body {
        param($context)
        $context.updateEndpoint='http://127.0.0.1:'+ $port + '/';$context.releaseRoot=$published;$context.chunkDelayMilliseconds=0
        $exe=(Get-Command pwsh).Source;$script=Join-Path $project 'Tool/Client/Serve-FrameworkReleases.ps1';$command=@{file=$exe;fileSha256=(Get-FrameworkFileHash $exe);script=$script;scriptSha256=(Get-FrameworkFileHash $script)}
        $files=@();foreach($item in (Get-FrameworkInventory (Join-Path $published 'Release')).GetEnumerator()){$files+=@{path=('Release/'+$item.Key);sha256=$item.Value.sha256}}
        $selection=Get-FrameworkReleaseSelection $published @{runId='serverqual';caseId='held';relativeDirectory='Release';files=$files} 'serverqual' 'held'
        $policy=New-FrameworkTransferPolicy (Join-Path $published 'Release') (Join-Path $published 'known.json') 'serverqual' 'held' 'Hotfix/Client.Game.dll' 'network-interruption'
        $server=$null;$client=$null;$response=$null;$stream=$null;$cancel=$null
        try{
            $server=Start-FrameworkReleaseServer $command $context (Join-Path $root 'server-hold');Select-FrameworkReleaseInput $server $selection;Set-FrameworkTransferPolicy $server $selection $policy
            $handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=$false;$client=[Net.Http.HttpClient]::new($handler);$cancel=[Threading.CancellationTokenSource]::new(5000)
            $response=$client.GetAsync(($context.updateEndpoint+$policy.relativePath),[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$cancel.Token).GetAwaiter().GetResult()
            Require ($response.Content.Headers.ContentLength -eq 100) 'Authenticated Content-Length changed.'
            $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();$bytes=[byte[]]::new([int]$policy.holdBytes);$received=0
            while($received -lt $bytes.Length){$n=$stream.ReadAsync($bytes,$received,$bytes.Length-$received,$cancel.Token).GetAwaiter().GetResult();Require ($n -gt 0) 'Prefix ended early.';$received+=$n}
            $watch=[Diagnostics.Stopwatch]::StartNew();$holding=$null
            while(-not $holding -and $watch.Elapsed.TotalSeconds -lt 2){$path=Get-ChildItem (Join-Path $server.outputDirectory 'transfers') -Recurse -Filter holding.json -File -ErrorAction SilentlyContinue|Select-Object -First 1 -ExpandProperty FullName;if($path){$holding=Read-FrameworkJson $path}else{Start-Sleep -Milliseconds 10}}
            Require ($holding.bytesSent -eq $received -and $received -eq 99) 'Expected actual strict prefix.'
            $null=Complete-FrameworkHeldTransfer $server $holding @{runId='serverqual';caseId='held';processId=$PID;role='QualificationHttpClient'} 'network-interruption'
            $ended=$false;try{$extra=[byte[]]::new(1);$n=$stream.ReadAsync($extra,0,1,$cancel.Token).GetAwaiter().GetResult();$ended=$n -eq 0}catch [IO.IOException]{$ended=$true}
            Require ($ended -and -not $cancel.IsCancellationRequested) 'Held connection did not terminate on server abort.'
        }finally{if($stream){$stream.Dispose()};if($response){$response.Dispose()};if($client){$client.Dispose()};if($cancel){$cancel.Dispose()};if($server -and -not $server.handle.HasExited){Stop-FrameworkReleaseServer $server}}
    }
    Require ($result.succeeded -and -not $result.nativeAcceptance) 'HTTP tool qualification claimed native acceptance.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='ToolIntegration';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records}
Write-Host "D2_SERVER_RECEIPT $root"
if($failed.Count){throw "D2_SERVER_FAILED $($failed.Count)/$($records.Count)"}
