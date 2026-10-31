#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'Tool/Client/Framework-Evidence.ps1')
. (Join-Path $project 'Tool/Client/Framework-IO.ps1')
. (Join-Path $PSScriptRoot 'Framework-Fixtures.ps1')
. (Join-Path $PSScriptRoot 'Framework-FileFixtures.ps1')
$scriptPath=Join-Path $project 'Tool/Client/Framework-Collector.ps1';if(Test-Path $scriptPath){. $scriptPath}
$root=Join-Path $project ('Builds/Tests/D2/Collector-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_COLLECTOR_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_COLLECTOR_FAIL $Name $($_.Exception.Message)"}}
function Require($Condition,[string]$Message){if(-not $Condition){throw $Message}}
$a=New-FrameworkFixtureAttempt;$context=New-FrameworkFixtureContext
$source=Join-Path $root ('native/'+$a.attemptId);New-Item -ItemType Directory -Path $source -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $source 'events.jsonl'),(($a.events|ForEach-Object {$_|ConvertTo-Json -Depth 30 -Compress})-join "`n")+"`n")
Check 'collector copies original bytes once with PID and attempt correlation' {
    $descriptor=Save-FrameworkAttemptEvidence $source $a.process $context (Join-Path $root 'captured')
    Require ($descriptor.attemptId -ceq $a.attemptId -and $descriptor.processId -eq 123) 'Collected native identity changed.'
    Require ($descriptor.events.sha256 -ceq (Get-FrameworkFileHash (Join-Path $source 'events.jsonl'))) 'Collected raw bytes differ.'
}
Check 'collector refuses overwrite and preserves first attempt bytes' {
    $target=Join-Path $root 'captured';$before=(Get-FileHash (Join-Path $target 'events.jsonl')).Hash
    $rejected=$false;try{Save-FrameworkAttemptEvidence $source $a.process $context $target|Out-Null}catch [IO.InvalidDataException]{$rejected=$true}
    Require ($rejected -and (Get-FileHash (Join-Path $target 'events.jsonl')).Hash -ceq $before) 'Prior attempt evidence was changed.'
}
Check 'collector rejects foreign process before any output is created' {
    $foreign=$a.process.Clone();$foreign.processId=999;$target=Join-Path $root 'foreign'
    $rejected=$false;try{Save-FrameworkAttemptEvidence $source $foreign $context $target|Out-Null}catch [IO.InvalidDataException]{$rejected=$true}
    Require ($rejected -and -not (Test-Path $target)) 'Foreign process produced a collector output.'
}
Check 'plan retains all required cases and separate kill recovery commands' {
    $plan=New-FrameworkCollectionPlan -Platform Windows64 -RunId d2unit -PlayerPath 'C:/native/Client.exe' -NativeEvidenceRoot 'C:/n/Acceptance'
    Require ($plan.cases.Count -eq 34 -and @($plan.cases|Where-Object status -eq NotRun).Count -eq 34) 'Required cases were omitted or pre-marked Pass.'
    $kill=@($plan.cases|Where-Object id -eq 'kill-state-replace-before')[0]
    Require ($kill.attempts.Count -eq 2 -and ($kill.attempts[0].command.arguments[1]|ConvertFrom-Json).statePurpose -ceq 'candidate' -and -not ($kill.attempts[1].command.arguments[1]|ConvertFrom-Json).pausePoint) 'Kill/recovery controls differ from frozen native options.'
}
Check 'execution gate refuses native effects before starting a tool' {
    $rejected=$false;try{Invoke-FrameworkCollection -Plan @{schemaVersion=1;cases=@()} -Execute|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Preparation invoked native execution.'
}
Check 'copied native-shaped attempt imports through file-bound bundle references' {
    $fixture=New-FrameworkFileBundle (Join-Path $root 'bundle-roundtrip')
    $archive=Read-FrameworkArchive $fixture.archive $fixture.result Windows64
    $directory=Join-Path $fixture.bundleRoot 'attempt'
    $captured=Save-FrameworkAttemptEvidence $source $a.process $context $directory
    $fixture.bundle.cases=@(@{id='update-v2';executed=$true;attempts=@(@{events=@{path='attempt/events.jsonl';sha256=$captured.events.sha256};process=@{path='attempt/owned-process.json';sha256=$captured.process.sha256}})})
    $fixture.bundle|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $fixture.bundlePath -Encoding utf8NoBOM
    $imported=Import-FrameworkEvidence $fixture.bundlePath $archive Windows64
    Assert-FrameworkAttempt $imported.cases[0].attempts[0] $imported 'update-v2'
    Require ($imported.cases[0].attempts[0].events.Count -eq $a.events.Count -and $imported.cases[0].attempts[0].events[0].utc -is [string]) 'Native JSONL scalar identity was coerced or lost.'
    [IO.File]::AppendAllText((Join-Path $directory 'events.jsonl'),'{"runId":"stale"}'+"`n")
    $rejected=$false;try{Import-FrameworkEvidence $fixture.bundlePath $archive Windows64|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Changed prior attempt bytes were accepted by the bundle.'
}
Check 'orchestration orders checkpoint kill collection before separate recovery' {
    $plan=New-FrameworkCollectionPlan Windows64 d2unit 'C:/native/Client.exe' 'C:/n/Acceptance'
    $plan.cases=@($plan.cases|Where-Object id -eq 'kill-download-partial')
    $script:nextProcess=100;$script:operations=@()
    $adapter={param($Operation,$Case,$Attempt,$Owned)
        $script:operations+=$Operation+':'+$Attempt.role
        if($Operation -eq 'Start'){$script:nextProcess++;return @{processId=$script:nextProcess;attemptId=([Guid]::NewGuid().ToString('N'));runId='d2unit';caseId=$Case.id}}
        if($Operation -eq 'WaitCheckpoint'){return @{runId='d2unit';caseId=$Case.id;attemptId=$Owned.attemptId;processId=$Owned.processId;point='download.partial';data=@{bytes=5;expectedBytes=10}}}
        return @{terminal=$true;processId=$Owned.processId;evidenceKind='SyntheticUnit'}
    }
    $result=Invoke-FrameworkCollection -Plan $plan -UnitAdapter $adapter
    $ordered='Prepare:primary,Start:primary,WaitCheckpoint:primary,KillOwned:primary,WaitTerminal:primary,Collect:primary,Start:recovery,WaitTerminal:recovery,Collect:recovery'
    Require (($script:operations -join ',') -ceq $ordered -and -not $result.nativeAcceptance -and $result.cases[0].status -ceq 'NotRun' -and $result.cases[0].attempts[0].processId -ne $result.cases[0].attempts[1].processId) 'Collector sequencing or scope was incorrect.'
}
Check 'slot snapshot uses exact signed file names and rejects truncated metadata' {
    $slot=Join-Path $root 'slot';$records=@()
    foreach($name in @('Hotfix/Client.Game.dll','Hotfix/Client.Game.Protocol.dll','Resources/GameFrameworkVersion.dat','Metadata/System.Runtime.CompilerServices.Unsafe.dll')){
        $path=Join-Path $slot $name;[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,'synthetic-'+$name);$records+=@{path=$name;length=(Get-Item $path).Length;sha256=(Get-FrameworkFileHash $path)}
    }
    Write-FrameworkJsonNew (Join-Path $slot 'release.manifest.json') @{releaseId='v2';files=$records}
    [IO.File]::WriteAllBytes((Join-Path $slot 'release.manifest.sig'),[byte[]]::new(256))
    $selection=@{ReleaseId='v2';Root=$slot;ManifestSha256=(Get-FrameworkFileHash (Join-Path $slot 'release.manifest.json'))}
    $snapshot=Get-FrameworkSlotSnapshot $slot $selection
    Require ($snapshot.metadata.ContainsKey('System.Runtime.CompilerServices.Unsafe.dll') -and $snapshot.files.Count -eq 6) 'Exact original metadata and manifest/signature bytes were not captured.'
    Move-Item -LiteralPath (Join-Path $slot 'Metadata/System.Runtime.CompilerServices.Unsafe.dll') -Destination (Join-Path $slot 'Metadata/System.Runtime.CompilerServices.Unsafe.dl')
    $rejected=$false;try{Get-FrameworkSlotSnapshot $slot $selection|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Truncated metadata filename was accepted.'
}
Check 'collection plan binds exact producer receipt and original runnable bytes' {
    $fixture=New-FrameworkFileBundle (Join-Path $root 'bound-plan');$archive=Read-FrameworkArchive $fixture.archive $fixture.result Windows64
    $playerRoot=Join-Path $fixture.archive 'Player';$plan=New-FrameworkCollectionPlan Windows64 d2unit (Join-Path $playerRoot 'Client.exe') 'C:/n/Acceptance'
    $bound=Bind-FrameworkCollectionPlan $plan $archive (Join-Path $fixture.bundleRoot 'receipt.json') $playerRoot
    Require ($bound.binding.playerBuildId -ceq 'player' -and $bound.binding.archiveSha256 -ceq $archive.archiveSha256 -and $bound.cases[0].attempts[0].command.sha256 -ceq $archive.archiveNative['Client.exe']) 'Native command was not bound to exact original producer bytes.'
    $tampered=ConvertFrom-FrameworkJson ($plan|ConvertTo-Json -Depth 100 -Compress) -ObjectOnly;$tampered.cases[0].attempts[0].command.arguments+=@('-unexpected-option','value')
    $rejected=$false;try{Bind-FrameworkCollectionPlan $tampered $archive (Join-Path $fixture.bundleRoot 'receipt.json') $playerRoot|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Unreviewed command tokens bypassed the bound launch recipe.'
    [IO.File]::AppendAllText((Join-Path $playerRoot 'GameAssembly.dll'),'changed')
    $rejected=$false;try{Bind-FrameworkCollectionPlan $plan $archive (Join-Path $fixture.bundleRoot 'receipt.json') $playerRoot|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Changed adjacent native DLL was accepted.'
}
Check 'owned startup guard failure can be preserved as diagnostic only' {
    $failed=New-FrameworkFixtureAttempt 'guard-failure';$failed.events=@($failed.events|Where-Object {$_.kind -in @('process.start','native.runtime')})
    ($failed.events|Where-Object kind -eq 'native.runtime').data.pointerBits=32
    Add-FrameworkFixtureEvent $failed 'host.failure' @{error='Native Player identity differs from actual runtime.'}
    for($i=0;$i -lt $failed.events.Count;$i++){$failed.events[$i].sequence=$i+1}
    $sourceDir=Join-Path $root ('guard-source/'+$failed.attemptId);New-Item -ItemType Directory -Path $sourceDir -Force|Out-Null
    $eventFile=Join-Path $sourceDir 'events.jsonl';[IO.File]::WriteAllText($eventFile,(($failed.events|ForEach-Object {$_|ConvertTo-Json -Depth 20 -Compress})-join "`n")+"`n")
    $hash=Get-FrameworkFileHash $eventFile
    $capture=Save-FrameworkAttemptEvidence $sourceDir $failed.process $context (Join-Path $root 'guard-capture') -DiagnosticCapture
    Require ($capture.schemaAccepted -eq $false -and $capture.nativeAcceptance -eq $false -and $capture.events.sha256 -ceq $hash -and (Get-FrameworkFileHash $eventFile) -ceq $hash) 'Guard failure was discarded, altered or labeled acceptance.'
    $copied=Read-FrameworkEvents (Join-Path $root 'guard-capture/events.jsonl')
    Require (@($copied|Where-Object kind -eq 'native.identity').Count -eq 0) 'Missing native identity was fabricated during diagnostic collection.'
}
Check 'preparation CLI emits a fully bound NotRun plan without native effects' {
    $fixture=New-FrameworkFileBundle (Join-Path $root 'prepare-cli');$output=Join-Path $root 'prepared'
    & (Get-Command pwsh).Source -NoProfile -File (Join-Path $project 'Tool/Client/Prepare-FrameworkRun.ps1') -Platform Windows64 -PlayerArchive $fixture.archive -BuildResult $fixture.result -BuildReceipt (Join-Path $fixture.bundleRoot 'receipt.json') -RuntimePlayerRoot (Join-Path $fixture.archive 'Player') -NativeEvidenceRoot 'C:/n/Acceptance' -RunId d2unit -OutputDirectory $output *> (Join-Path $root 'prepare-cli.log')
    Require ($LASTEXITCODE -eq 0) 'Preparation CLI failed.'
    $plan=Read-FrameworkJson (Join-Path $output 'execution-plan.json')
    Require ($plan.scope -ceq 'PreparationOnly' -and -not $plan.nativeAcceptance -and -not $plan.nativeExecutionEnabled -and $plan.cases.Count -eq 34 -and @($plan.cases|Where-Object status -eq NotRun).Count -eq 34) 'Preparation plan claimed actual native execution.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);cases=$records;script=@{path=$PSCommandPath;sha256=(Get-FrameworkFileHash $PSCommandPath)}}
Write-Host "D2_COLLECTOR_RECEIPT $root"
if($failed.Count){throw "D2_COLLECTOR_FAILED $($failed.Count)/$($records.Count)"}
