$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1";. "$project/Tool/Client/Framework-IO.ps1";. "$PSScriptRoot/Framework-Fixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/BackgroundRestore-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_BG_RESTORE_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_BG_RESTORE_FAIL $Name $($_.Exception.Message)"}}
Check 'background platform deactivation permits inactive caret reset only when editing state restores' {
 $case=New-FrameworkFixtureUi 'background';$middle=@($case.attempts[0].events|Where-Object kind -ceq 'ui.sample')[1]
 $middle.data.snapshot.input.caret=0;$middle.data.snapshot.input.selectionAnchor=0;$middle.data.snapshot.input.selectionFocus=0
 Assert-FrameworkUi $case @{action='background'}
}
Check 'background final active caret and selection must match original editing state' {
 $case=New-FrameworkFixtureUi 'background';$last=@($case.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1];$last.data.snapshot.input.caret=99
 $caught=$false;try{Assert-FrameworkUi $case @{action='background'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Wrong final editing caret was accepted.'
}
Check 'background inactive text changes remain forbidden' {
 $case=New-FrameworkFixtureUi 'background';$middle=@($case.attempts[0].events|Where-Object kind -ceq 'ui.sample')[1];$middle.data.snapshot.input.textSha256='0'*64
 $caught=$false;try{Assert-FrameworkUi $case @{action='background'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Changed background account text was accepted.'
}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{scope='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_BG_RESTORE_RECEIPT $root";if($failed.Count){throw "D2_BG_RESTORE_FAILED $($failed.Count)/$($records.Count)"}
