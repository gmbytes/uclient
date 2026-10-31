#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
$root=Join-Path $project ('Builds/Tests/D2/AndroidCheckpoint-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_CHECKPOINT_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_CHECKPOINT_FAIL $Name $($_.Exception.Message)"}}
function Owned {return @{processId=123;runId='qual';caseId='checkpoint';acceptance=@{pausePoint='hotentry.executing'}}}
function Point {return @{runId='qual';caseId='checkpoint';attemptId=('a'*32);processId=123;point='hotentry.executing';purpose=$null;token=('qual:checkpoint:'+('a'*32)+':123:hotentry.executing:');data=@{message='entry'}}}
Check 'exact native checkpoint binds the owned process and requested boundary' {Assert-FrameworkCheckpoint (Point) (Owned) ('a'*32)}
foreach($field in @('runId','caseId','attemptId','processId','point','purpose','token')){
    Check "foreign native checkpoint rejects $field" {$p=Point;$p[$field]=if($field -eq 'processId'){124}else{'foreign'};$caught=$false;try{Assert-FrameworkCheckpoint $p (Owned) ('a'*32)}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign checkpoint was accepted.'}
}
Check 'state checkpoint must bind the intended write purpose' {
    $owner=Owned;$owner.acceptance.pausePoint='state.replace.after';$owner.acceptance.statePurpose='candidate';$p=Point;$p.point='state.replace.after';$p.purpose='candidate';$p.token='qual:checkpoint:'+('a'*32)+':123:state.replace.after:candidate';Assert-FrameworkCheckpoint $p $owner ('a'*32)
    $p.purpose='promote';$caught=$false;try{Assert-FrameworkCheckpoint $p $owner ('a'*32)}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Different write purpose was accepted.'
}
Check 'partial download requires actual positive incomplete authenticated bytes' {
    $owner=Owned;$owner.acceptance.pausePoint='download.partial';$p=Point;$p.point='download.partial';$p.token='qual:checkpoint:'+('a'*32)+':123:download.partial:';$p.data=@{bytes=5;expectedBytes=10};Assert-FrameworkCheckpoint $p $owner ('a'*32)
    foreach($bytes in @(0,10,11)){$p.data.bytes=$bytes;$caught=$false;try{Assert-FrameworkCheckpoint $p $owner ('a'*32)}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Non-partial byte count was accepted.'}
}
Check 'unscoped Android checkpoint wait rejects before launching a helper' {
    $caught=$false;try{Wait-FrameworkAndroidCheckpoint -Tool @{} -Owned @{} -Context @{} -OutputDirectory (Join-Path $root 'forbidden')}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_PREPARATION_GATE*'}
    Require ($caught -and -not(Test-Path (Join-Path $root 'forbidden'))) 'Unscoped checkpoint wait caused effects.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records}
Write-Host "D2_CHECKPOINT_RECEIPT $root"
if($failed.Count){throw "D2_CHECKPOINT_FAILED $($failed.Count)/$($records.Count)"}
