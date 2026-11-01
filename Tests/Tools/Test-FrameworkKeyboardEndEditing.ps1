$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1";. "$project/Tool/Client/Framework-IO.ps1";. "$PSScriptRoot/Framework-Fixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/KeyboardEndEditing-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_KEYBOARD_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_KEYBOARD_FAIL $Name $($_.Exception.Message)"}}
function EndEditingCase {
 $case=New-FrameworkFixtureUi 'keyboard'
 $points=@($case.attempts[0].events|Where-Object {$_.kind -in @('ui.sample','ui.stable')})
 $points[0].data.snapshot.keyboardVisible=$true
 $points[1].data.snapshot.keyboardVisible=$false
 foreach($point in $points[2..($points.Count-1)]){$point.data.snapshot.keyboardVisible=$false;$point.data.snapshot.input.focused=$false;$point.data.snapshot.input.caret=0;$point.data.snapshot.input.selectionAnchor=0;$point.data.snapshot.input.selectionFocus=0}
 return $case
}
Check 'closing IME ends editing while retaining original account and input instance' {Assert-FrameworkUi (EndEditingCase) @{action='keyboard'}}
$negatives=[ordered]@{
 ChangedText={param($c)@($c.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1].data.snapshot.input.textSha256='0'*64}
 ChangedView={param($c)@($c.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1].data.snapshot.view=999}
 ActiveCaretLost={param($c)@($c.attempts[0].events|Where-Object kind -ceq 'ui.sample')[1].data.snapshot.input.caret=0}
 FocusLostBeforeClose={param($c)@($c.attempts[0].events|Where-Object kind -ceq 'ui.sample')[0].data.snapshot.input.focused=$false}
 StillEditingAfterClose={param($c)$p=@($c.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1];$p.data.snapshot.input.focused=$true;$p.data.snapshot.input.caret=4;$p.data.snapshot.input.selectionAnchor=4;$p.data.snapshot.input.selectionFocus=4}
 NoOpenedKeyboard={param($c)foreach($p in $c.attempts[0].events|Where-Object {$_.kind -like 'ui.*'}){$p.data.snapshot.keyboardVisible=$false}}
 NoFinalClose={param($c)@($c.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1].data.snapshot.keyboardVisible=$true}
}
foreach($name in $negatives.Keys){$mutation=$negatives[$name];Check "end editing rejects $name" {$case=EndEditingCase;&$mutation $case;$caught=$false;try{Assert-FrameworkUi $case @{action='keyboard'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Invalid end-editing evidence was accepted.'}}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{scope='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_KEYBOARD_RECEIPT $root";if($failed.Count){throw "D2_KEYBOARD_FAILED $($failed.Count)/$($records.Count)"}
