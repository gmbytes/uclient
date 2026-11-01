# Native-shaped synthetic files exercise provenance rules; they never count as native acceptance.
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1";. "$project/Tool/Client/Framework-IO.ps1";. "$project/Tool/Client/Framework-NativeAdapter.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1";. "$PSScriptRoot/Framework-ExecutionFixtures.ps1"
$root=Join-Path $project ('Builds/Tests/D2/AcceptanceSource-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Require($v,[string]$message){if(-not$v){throw $message}}
function Check([string]$name,[scriptblock]$body){try{&$body;$script:records+=@{name=$name;passed=$true};Write-Host "D2_SOURCE_PASS $name"}catch{$script:records+=@{name=$name;passed=$false;error=$_.Exception.Message};Write-Host "D2_SOURCE_FAIL $name $($_.Exception.Message)"}}
function Fixture {
 $f=New-FrameworkExecutionFixture $root ([Guid]::NewGuid().ToString('N'))
 foreach($proof in $f.gate.d1Evidence){$relative='d1-'+$proof.version+'.jsonl';[IO.File]::Copy($proof.path,(Join-Path $f.fixture.bundleRoot $relative));$proof.path=$relative}
 $gatePath=Join-Path $f.fixture.bundleRoot 'gate.json';Write-FrameworkJsonNew $gatePath $f.gate
 $f.fixture.bundle.evidenceKind='Native';$f.fixture.bundle.executionGate=@{path='gate.json';sha256=(Get-FrameworkFileHash $gatePath)}
 [IO.File]::WriteAllText($f.fixture.bundlePath,($f.fixture.bundle|ConvertTo-Json -Depth 30))
 $f.evidence=Import-FrameworkEvidence $f.fixture.bundlePath $f.archive Windows64
 return $f
}
function Reject([scriptblock]$body,[string]$code){$caught=$false;try{&$body|Out-Null}catch [IO.InvalidDataException]{$caught=$_.Exception.Message.StartsWith($code)};Require $caught "Expected $code refusal."}
Check 'reimport validates exact file source rather than mutable caller cases' {$f=Fixture;$f.evidence.cases=@(@{id='invented';executed=$true});$e=Read-FrameworkAcceptanceSource $f.evidence;Require ($e.evidenceKind -ceq 'Native' -and $e.cases.Count -eq 0) 'Canonical source was not reimported.'}
Check 'missing source cannot be replaced by approval booleans' {Reject {Read-FrameworkAcceptanceSource @{evidenceKind='Native';executionGate=@{finalD1ArchivesApproved=$true}}} 'E_NATIVE_SOURCE'}
Check 'source bytes changed after import fail closed' {$f=Fixture;[IO.File]::AppendAllText($f.fixture.bundlePath,' ');Reject {Read-FrameworkAcceptanceSource $f.evidence} 'E_NATIVE_SOURCE'}
Check 'archive changed after import fails closed' {$f=Fixture;[IO.File]::AppendAllText((Join-Path $f.fixture.archive 'archive.json'),' ');Reject {Read-FrameworkAcceptanceSource $f.evidence} 'E_NATIVE_SOURCE'}
Check 'synthetic source cannot be promoted through a caller native flag' {$f=New-FrameworkFileBundle (Join-Path $root 'synthetic');$a=Read-FrameworkArchive $f.archive $f.result Windows64;$e=Import-FrameworkEvidence $f.bundlePath $a Windows64;$e.evidenceKind='Native';Reject {Read-FrameworkAcceptanceSource $e} 'E_NATIVE_SOURCE'}
Check 'canonical native source with missing matrix remains NotRun' {$f=Fixture;$r=Get-FrameworkAcceptance $f.evidence;Require (-not$r.passed -and @($r.errors|Where-Object {$_ -like 'E_PREPARATION_GATE*'}).Count -eq 0 -and @($r.cases|Where-Object status -ceq 'NotRun').Count -eq 34) 'A static gate masked native provenance or missing cases were accepted.'}
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;count=$records.Count;passed=(@($records|Where-Object passed -eq $false).Count -eq 0);cases=$records}
Write-Host "D2_SOURCE_RECEIPT $root"
if(@($records|Where-Object passed -eq $false).Count){throw 'D2_SOURCE_FAILED'}
