#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/NativeSession-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_SESSION_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_SESSION_FAIL $Name $($_.Exception.Message)"}}
function Fixture([string]$Name){New-FrameworkFileBundle (Join-Path $root $Name)}
function Run($Fixture,[string]$Name,[scriptblock]$Body){Invoke-FrameworkNativeQualification -Platform Windows64 -RunId d2qual -PlayerArchive $Fixture.archive -BuildResult $Fixture.result -RuntimePlayerRoot (Join-Path $Fixture.archive 'Player') -OutputDirectory (Join-Path $root $Name) -Body $Body}
function IsBlocked {try{Assert-FrameworkEffectsReleased;return $false}catch [IO.InvalidDataException]{return $_.Exception.Message -like 'E_PREPARATION_GATE*'}}
Check 'qualification binds archive and is never acceptance' {
    $f=Fixture 'valid';$r=Run $f 'valid-run' {param($context) Assert-FrameworkEffectsReleased;Require ($context.playerBuildId -ceq 'player' -and $context.runId -ceq 'd2qual') 'Wrong native qualification identity.';return 'body-result'}
    Require ($r.succeeded -eq $true -and $r.nativeAcceptance -eq $false -and $r.nativeUnchanged -eq $true -and $r.output -ceq 'body-result') 'Qualification was lost or mislabeled acceptance.'
    Require (IsBlocked) 'Native gate remained open after qualification.'
}
Check 'unscoped effects remain blocked' {Require (IsBlocked) 'Unscoped native effect was allowed.'}
Check 'nested qualification cannot replace a live owner' {
    $f=Fixture 'nested';$r=Run $f 'nested-run' {param($context) $rejected=$false;try{Run $f 'inner-run' {throw 'Should not execute'}}catch [IO.InvalidDataException]{$rejected=$_.Exception.Message -like 'E_QUALIFICATION_NESTED*'};Require $rejected 'Nested owner was allowed.';Assert-FrameworkEffectsReleased}
    Require ($r.succeeded -and -not(Test-Path (Join-Path $root 'inner-run'))) 'Nested qualification had effects.'
}
Check 'body failure closes effects and preserves a failed receipt' {
    $f=Fixture 'failure';$caught=$false;try{Run $f 'failure-run' {throw [IO.IOException]::new('qualification body failure')}}catch [IO.IOException]{$caught=$_.Exception.Message -ceq 'qualification body failure'}
    $receipt=Read-FrameworkJson (Join-Path $root 'failure-run/qualification-result.json')
    Require ($caught -and (IsBlocked) -and -not $receipt.succeeded -and -not $receipt.nativeAcceptance -and $receipt.nativeUnchanged) 'Failure lost provenance or leaked an open gate.'
}
Check 'changed runnable native input rejects before body' {
    $f=Fixture 'changed-before';[IO.File]::AppendAllText((Join-Path $f.archive 'Player/GameAssembly.dll'),'changed');$script:entered=$false
    $caught=$false;try{Run $f 'changed-before-run' {$script:entered=$true}}catch [IO.InvalidDataException]{$caught=$true}
    Require ($caught -and -not $script:entered -and -not(Test-Path (Join-Path $root 'changed-before-run'))) 'Changed native input reached an effect body.'
}
Check 'native mutation during body is rejected and retained' {
    $f=Fixture 'changed-after';$caught=$false;try{Run $f 'changed-after-run' {param($context) [IO.File]::AppendAllText((Join-Path $context.runtimePlayerRoot 'GameAssembly.dll'),'changed')}}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_QUALIFICATION_NATIVE_CHANGED*'}
    $receipt=Read-FrameworkJson (Join-Path $root 'changed-after-run/qualification-result.json')
    Require ($caught -and -not $receipt.nativeUnchanged -and -not $receipt.succeeded -and (IsBlocked)) 'Mutation was accepted or its gate remained open.'
}
Check 'mutable caller context cannot change the active run binding' {
    $f=Fixture 'changed-context';$r=Run $f 'changed-context-run' {param($context) $context.runId='foreign';Require (IsBlocked) 'Changed run context was accepted.'}
    Require (-not $r.nativeAcceptance -and (IsBlocked)) 'Qualification granted acceptance.'
}
Check 'unbounded run identifiers reject before output' {
    $f=Fixture 'bad-run';$caught=$false;try{Invoke-FrameworkNativeQualification -Platform Windows64 -RunId '../foreign' -PlayerArchive $f.archive -BuildResult $f.result -RuntimePlayerRoot (Join-Path $f.archive 'Player') -OutputDirectory (Join-Path $root 'bad-run-output') -Body {throw 'Must not execute'}}catch [IO.InvalidDataException]{$caught=$true}
    Require ($caught -and -not(Test-Path (Join-Path $root 'bad-run-output'))) 'Invalid run entered native scope.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records}
Write-Host "D2_SESSION_RECEIPT $root"
if($failed.Count){throw "D2_SESSION_FAILED $($failed.Count)/$($records.Count)"}
