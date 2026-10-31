#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/ExecutionGate-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_EXECUTION_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_EXECUTION_FAIL $Name $($_.Exception.Message)"}}
. "$PSScriptRoot/Framework-ExecutionFixtures.ps1"
function Fixture([string]$Name){New-FrameworkExecutionFixture $root $Name}
function Run($F,[string]$Output,[scriptblock]$Body){Invoke-FrameworkNativeExecution -Platform Windows64 -RunId matrix -PlayerArchive $F.fixture.archive -BuildResult $F.fixture.result -RuntimePlayerRoot (Join-Path $F.fixture.archive 'Player') -ExecutionGate $F.gatePath -OutputDirectory (Join-Path $root $Output) -Body $Body}
function SaveGate($F){[IO.File]::WriteAllText($F.gatePath,($F.gate|ConvertTo-Json -Depth 30))}
Check 'execution requires bound D1 raw facts and grants no matrix pass' {$f=Fixture 'valid';$r=Run $f 'valid-run' {param($context) Assert-FrameworkEffectsReleased;Require ($context.scope -ceq 'NativeExecution' -and $context.playerBuildId -ceq 'player') 'Wrong execution context.'};Require ($r.scope -ceq 'NativeExecution' -and $r.succeeded -eq $true -and $r.nativeAcceptance -eq $false) 'Scope creation claimed native acceptance.'}
foreach($field in @('status','finalD1ArchivesApproved','platform','playerBuildId','archiveSha256','producerCommit')){
    Check "foreign or unreleased gate rejects $field" {$f=Fixture ('bad-'+$field);$f.gate[$field]=if($field -ceq 'finalD1ArchivesApproved'){$false}else{'invalid'};SaveGate $f;$caught=$false;try{Run $f ('bad-run-'+$field) {throw 'Must not execute'}}catch [IO.InvalidDataException]{$caught=$true};Require ($caught -and -not(Test-Path (Join-Path $root ('bad-run-'+$field)))) 'Invalid gate entered execution.'}
}
Check 'approval flag alone cannot replace original D1 events' {$f=Fixture 'no-events';$f.gate.d1Evidence=@();SaveGate $f;$caught=$false;try{Run $f 'no-events-run' {throw 'Must not execute'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Boolean approval bypassed D1 proof.'}
Check 'changed raw D1 bytes reject even with approval' {$f=Fixture 'changed';[IO.File]::AppendAllText($f.gate.d1Evidence[1].path,'changed');$caught=$false;try{Run $f 'changed-run' {throw 'Must not execute'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Changed proof bytes were accepted.'}
Check 'nonready D1 V2 rejects despite valid proof hash' {$f=Fixture 'not-ready';$path=$f.gate.d1Evidence[1].path;$s=[IO.File]::ReadAllText($path).Replace('"ready":true','"ready":false');[IO.File]::WriteAllText($path,$s);$f.gate.d1Evidence[1].sha256=Get-FrameworkFileHash $path;SaveGate $f;$caught=$false;try{Run $f 'not-ready-run' {throw 'Must not execute'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Failed D1 launch enabled execution.'}
Check 'body exception closes the native effect scope' {$f=Fixture 'failure';$caught=$false;try{Run $f 'failure-run' {throw [IO.IOException]::new('matrix body error')}}catch [IO.IOException]{$caught=$true};$closed=$false;try{Assert-FrameworkEffectsReleased}catch [IO.InvalidDataException]{$closed=$true};Require ($caught -and $closed) 'Failure leaked active effects.'}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;count=$records.Count;passed=($failed.Count -eq 0);cases=$records};Write-Host "D2_EXECUTION_RECEIPT $root";if($failed.Count){throw "D2_EXECUTION_FAILED $($failed.Count)/$($records.Count)"}
