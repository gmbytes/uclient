$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1";. "$project/Tool/Client/Framework-IO.ps1"
$root=Join-Path $project ('Builds/Tests/D2/NativeProvenance-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_PROVENANCE_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_PROVENANCE_FAIL $Name $($_.Exception.Message)"}}
function Fixture {
 $dir=Join-Path $root ([Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory((Join-Path $dir 'capture'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $dir 'capture/events.jsonl'),'native-shaped-unit-stream')
 Write-FrameworkJsonNew (Join-Path $dir 'capture/owned-process.json') @{runId='run';caseId='case';processId=123;attemptId=('a'*32);terminal=$true;playerBuildId='player'}
 $eventRef=@{path='events.jsonl';sha256=(Get-FrameworkFileHash (Join-Path $dir 'capture/events.jsonl'))};$processRef=@{path='owned-process.json';sha256=(Get-FrameworkFileHash (Join-Path $dir 'capture/owned-process.json'))}
 $descriptor=@{evidenceKind='Native';schemaAccepted=$true;processId=123;attemptId=('a'*32);events=$eventRef;process=$processRef}
 Write-FrameworkJsonNew (Join-Path $dir 'capture/collector-receipt.json') $descriptor
 $receipt=@{path='capture/collector-receipt.json';sha256=(Get-FrameworkFileHash (Join-Path $dir 'capture/collector-receipt.json'))}
 $source=@{schemaVersion=1;evidenceKind='NativeCollection';nativeAcceptance=$false;runId='run';platform='Windows64';playerBuildId='player';cases=@(@{id='case';taskId='D2';status='Collected';executed=$true;attempts=@(@{role='primary';processId=123;attemptId=('a'*32);capture=@{evidenceKind='Native';schemaAccepted=$true;receipt=@{path='C:/original/capture/collector-receipt.json';sha256=$receipt.sha256}};terminal=@{processId=123;terminal=$true}})})}
 Write-FrameworkJsonNew (Join-Path $dir 'original-collection.json') $source
 $proof=@{schemaVersion=1;scope='NativeCollectionExport';caseId='case';source=@{path='original-collection.json';sha256=(Get-FrameworkFileHash (Join-Path $dir 'original-collection.json'))};attempts=@(@{role='primary';receipt=$receipt})}
 Write-FrameworkJsonNew (Join-Path $dir 'collection-proof.json') $proof
 return @{root=$dir;reference=@{path='collection-proof.json';sha256=(Get-FrameworkFileHash (Join-Path $dir 'collection-proof.json'))};case=@{id='case';attempts=@(@{processId=123;attemptId=('a'*32);eventsSha256=$eventRef.sha256;processSha256=$processRef.sha256})};archive=@{playerBuildId='player'};proof=$proof;source=$source}
}
function Evaluate($f){Import-FrameworkNativeCollectionProof $f.root $f.reference $f.case $f.archive 'run' 'Windows64'}
function Rewrite($f){$path=Join-Path $f.root 'original-collection.json';[IO.File]::WriteAllText($path,($f.source|ConvertTo-Json -Depth 20));$f.proof.source.sha256=Get-FrameworkFileHash $path;$path=Join-Path $f.root 'collection-proof.json';[IO.File]::WriteAllText($path,($f.proof|ConvertTo-Json -Depth 20));$f.reference.sha256=Get-FrameworkFileHash $path}
Check 'portable native collection proof binds original source and exact capture bytes' {$f=Fixture;$r=Evaluate $f;Require ($r.validated -eq $true -and $r.caseId -ceq 'case') 'Verified collection binding missing.'}
foreach($field in @('runId','platform','playerBuildId','evidenceKind')){Check "collection rejects foreign $field" {$f=Fixture;$f.source[$field]='foreign';Rewrite $f;$caught=$false;try{Evaluate $f|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign collection accepted.'}}
Check 'collected status cannot substitute for executed native attempts' {$f=Fixture;$f.source.cases[0].executed=$false;Rewrite $f;$caught=$false;try{Evaluate $f|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Unexecuted collection accepted.'}
Check 'changed original collector receipt rejects even with updated outer proof' {$f=Fixture;[IO.File]::AppendAllText((Join-Path $f.root 'capture/collector-receipt.json'),' ');$f.proof.attempts[0].receipt.sha256=Get-FrameworkFileHash (Join-Path $f.root 'capture/collector-receipt.json');Rewrite $f;$caught=$false;try{Evaluate $f|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Changed original receipt accepted.'}
Check 'collection capture events must match imported actual attempt bytes' {$f=Fixture;$f.case.attempts[0].eventsSha256='0'*64;$caught=$false;try{Evaluate $f|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Unrelated events accepted.'}
Check 'native process must be terminal in original collection' {$f=Fixture;$f.source.cases[0].attempts[0].terminal.terminal=$false;Rewrite $f;$caught=$false;try{Evaluate $f|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Live native process accepted.'}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_PROVENANCE_RECEIPT $root";if($failed.Count){throw "D2_PROVENANCE_FAILED $($failed.Count)/$($records.Count)"}
