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
    foreach($name in @('Hotfix/Client.Game.dll','Resources/GameFrameworkVersion.dat','Metadata/System.Runtime.CompilerServices.Unsafe.dll')){
        $path=Join-Path $slot $name;[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,'synthetic-'+$name);$records+=@{path=$name;length=(Get-Item $path).Length;sha256=(Get-FrameworkFileHash $path)}
    }
    Write-FrameworkJsonNew (Join-Path $slot 'release.manifest.json') @{releaseId='v2';files=$records}
    [IO.File]::WriteAllBytes((Join-Path $slot 'release.manifest.sig'),[byte[]]::new(256))
    $selection=@{ReleaseId='v2';Root=$slot;ManifestSha256=(Get-FrameworkFileHash (Join-Path $slot 'release.manifest.json'))}
    $snapshot=Get-FrameworkSlotSnapshot $slot $selection
    Require ($snapshot.metadata.ContainsKey('System.Runtime.CompilerServices.Unsafe.dll') -and $snapshot.files.Count -eq 5) 'Exact original metadata and manifest/signature bytes were not captured.'
    Move-Item -LiteralPath (Join-Path $slot 'Metadata/System.Runtime.CompilerServices.Unsafe.dll') -Destination (Join-Path $slot 'Metadata/System.Runtime.CompilerServices.Unsafe.dl')
    $rejected=$false;try{Get-FrameworkSlotSnapshot $slot $selection|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Truncated metadata filename was accepted.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);cases=$records;script=@{path=$PSCommandPath;sha256=(Get-FrameworkFileHash $PSCommandPath)}}
Write-Host "D2_COLLECTOR_RECEIPT $root"
if($failed.Count){throw "D2_COLLECTOR_FAILED $($failed.Count)/$($records.Count)"}
