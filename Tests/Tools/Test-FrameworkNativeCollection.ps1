#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
. "$project/Tool/Client/Framework-Collector.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1"
. "$PSScriptRoot/Framework-ExecutionFixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/NativeCollection-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_NATIVE_COLLECT_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_NATIVE_COLLECT_FAIL $Name $($_.Exception.Message)"}}
function Plan($F){$plan=New-FrameworkCollectionPlan Windows64 nativecollect 'C:/native/Client.exe' 'C:/n/Acceptance';$plan.cases=@($plan.cases|Where-Object id -ceq 'baseline-offline');$plan.binding=@{playerBuildId='player';archiveSha256=$F.archive.archiveSha256;runtimePlayerRoot=(Join-Path $F.fixture.archive 'Player')};return $plan}
function Scope($F,[string]$Name,[scriptblock]$Action){Invoke-FrameworkNativeExecution -Platform Windows64 -RunId nativecollect -PlayerArchive $F.fixture.archive -BuildResult $F.fixture.result -RuntimePlayerRoot (Join-Path $F.fixture.archive 'Player') -ExecutionGate $F.gatePath -OutputDirectory (Join-Path $root $Name) -Body $Action}
Check 'native collection remains closed outside a released execution scope' {$caught=$false;try{Invoke-FrameworkNativeCollection -Plan @{schemaVersion=1} -NativeAdapter {throw 'Must not execute'}|Out-Null}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_PREPARATION_GATE*'};Require $caught 'Unscoped native collection was allowed.'}
Check 'foreign plan rejects before invoking an adapter' {$f=New-FrameworkExecutionFixture $root 'foreign';$plan=Plan $f;$plan.runId='foreign';$script:entered=$false;$r=Scope $f 'foreign-run' {param($context) $caught=$false;try{Invoke-FrameworkNativeCollection -Plan $plan -NativeAdapter {$script:entered=$true}|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require ($caught -and -not $script:entered) 'Foreign plan reached an adapter.'};Require $r.succeeded 'Boundary test failed.'}
Check 'adapter failure is retained while required case remains unproven' {$f=New-FrameworkExecutionFixture $root 'failure';$plan=Plan $f;$r=Scope $f 'failure-run' {param($context) Invoke-FrameworkNativeCollection -Plan $plan -NativeAdapter {param($operation) if($operation -ceq 'Prepare'){throw [IO.IOException]::new('actual adapter failure')}}};$collection=$r.output;Require ($collection.evidenceKind -ceq 'NativeCollection' -and -not $collection.nativeAcceptance -and $collection.cases[0].status -ceq 'Fail' -and -not $collection.cases[0].executed -and $collection.cases[0].error -like '*actual adapter failure*') 'Collector discarded failure or promoted incomplete work.'}
Check 'synthetic capture cannot be labeled collected native evidence' {$f=New-FrameworkExecutionFixture $root 'synthetic';$plan=Plan $f;$adapter={param($operation,$case,$attempt,$owned) switch($operation){'Start'{return @{processId=123;attemptId=('a'*32);runId='nativecollect';caseId=$case.id}}'WaitTerminal'{return @{terminal=$true;processId=123}}'Collect'{return @{evidenceKind='SyntheticUnit';schemaAccepted=$true}}}};$r=Scope $f 'synthetic-run' {param($context) Invoke-FrameworkCollection -Plan $plan -Execute -NativeAdapter $adapter};Require ($r.output.cases[0].status -ceq 'Fail' -and -not $r.output.cases[0].executed -and -not $r.output.nativeAcceptance) 'Synthetic capture was accepted as native.'}
Check 'file-bound native-shaped fixture is collected but never passes acceptance' {
    $f=New-FrameworkExecutionFixture $root 'captured';$plan=Plan $f;$captureRoot=Join-Path $root 'capture';[IO.Directory]::CreateDirectory($captureRoot)|Out-Null
    $events=Read-FrameworkEvents $f.gate.d1Evidence[0].path
    foreach($event in $events){$event.runId='nativecollect';$event.caseId='baseline-offline';$event.attemptId='b'*32;$event.processId=123}
    $eventPath=Join-Path $captureRoot 'events.jsonl';[IO.File]::WriteAllText($eventPath,(($events|ForEach-Object{$_|ConvertTo-Json -Depth 20 -Compress})-join "`n")+"`n")
    $owner=@{runId='nativecollect';caseId='baseline-offline';processId=123;playerBuildId='player';terminal=$true;startedUtc='2026-10-30T23:59:59Z';finishedUtc='2026-10-31T00:00:02Z'}
    $processPath=Join-Path $captureRoot 'owned-process.json';Write-FrameworkJsonNew $processPath $owner
    $descriptor=@{evidenceKind='Native';schemaAccepted=$true;nativeAcceptance=$false;attemptId=('b'*32);processId=123;events=@{path='events.jsonl';sha256=(Get-FrameworkFileHash $eventPath)};process=@{path='owned-process.json';sha256=(Get-FrameworkFileHash $processPath)}}
    $descriptorPath=Join-Path $captureRoot 'collector-receipt.json';Write-FrameworkJsonNew $descriptorPath $descriptor
    $capture=@{evidenceKind='Native';schemaAccepted=$true;receipt=@{path=$descriptorPath;sha256=(Get-FrameworkFileHash $descriptorPath)}}
    $adapter={param($operation,$case,$attempt,$owned) switch($operation){'Start'{return @{processId=123;runId='nativecollect';caseId=$case.id}}'WaitTerminal'{return @{terminal=$true;processId=123}}'Collect'{return $capture}}}
    $r=Scope $f 'captured-run' {param($context) Invoke-FrameworkCollection -Plan $plan -Execute -NativeAdapter $adapter}
    Require ($r.output.cases[0].status -ceq 'Collected' -and $r.output.cases[0].executed -eq $true -and $r.output.cases[0].attempts.Count -eq 1 -and $r.output.nativeAcceptance -eq $false) ('File-bound capture failed or incorrectly claimed acceptance: '+$r.output.cases[0].error)
}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_NATIVE_COLLECT_RECEIPT $root";if($failed.Count){throw "D2_NATIVE_COLLECT_FAILED $($failed.Count)/$($records.Count)"}
