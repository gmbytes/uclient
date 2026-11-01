$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1";. "$project/Tool/Client/Framework-IO.ps1";. "$PSScriptRoot/Framework-Fixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/ShortScroll-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_SHORT_SCROLL_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_SHORT_SCROLL_FAIL $Name $($_.Exception.Message)"}}
function ShortScrollCase {
 $case=New-FrameworkFixtureUi 'scroll'|ConvertTo-Json -Depth 60|ConvertFrom-Json -AsHashtable
 $points=@($case.attempts[0].events|Where-Object {$_.kind -in @('ui.sample','ui.stable')})
 foreach($point in $points){$point.data.snapshot.scrolls[0].y=0}
 # Native short content can move under Unrestricted while uGUI returns normalizedPosition=0.
 foreach($point in $points[1..($points.Count-1)]){foreach($bounds in @($point.data.snapshot.input.bounds)+@($point.data.snapshot.buttons|ForEach-Object bounds)){$bounds.bottom+=20;$bounds.top+=20}}
 return $case
}
Check 'short content scroll is proved by consistent original content translation inside stationary viewport' {Assert-FrameworkUi (ShortScrollCase) @{action='scroll'}}
$negatives=[ordered]@{
 NoMovement={param($s)foreach($b in @($s.input.bounds)+@($s.buttons|ForEach-Object bounds)){$b.top-=20;$b.bottom-=20}}
 InputOnly={param($s)foreach($b in @($s.buttons|ForEach-Object bounds)){$b.top-=20;$b.bottom-=20}}
 ButtonsOnly={param($s)$s.input.bounds.top-=20;$s.input.bounds.bottom-=20}
 ViewportMoved={param($s)$s.scrolls[0].bounds.top+=20;$s.scrolls[0].bounds.bottom+=20}
 ReplacedButton={param($s)$s.buttons[0].id=999}
 InputResized={param($s)$s.input.bounds.top+=10}
 HorizontalTranslation={param($s)$s.input.bounds.left+=10;$s.input.bounds.right+=10}
}
foreach($name in $negatives.Keys){$mutation=$negatives[$name];Check "short scroll rejects $name" {$case=ShortScrollCase;&$mutation @($case.attempts[0].events|Where-Object kind -ceq 'ui.stable')[-1].data.snapshot;$caught=$false;try{Assert-FrameworkUi $case @{action='scroll'}}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Invalid scroll geometry was accepted.'}}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{scope='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_SHORT_SCROLL_RECEIPT $root";if($failed.Count){throw "D2_SHORT_SCROLL_FAILED $($failed.Count)/$($records.Count)"}
