#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
$root=Join-Path $project ('Builds/Tests/D2/WindowsCompletion-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
$records=@()
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_WIN_COMPLETION_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_WIN_COMPLETION_FAIL $Name $($_.Exception.Message)"}}
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Owner {return @{runId='run';caseId='case';processId=123}}
function Events {
 $items=@(
  @{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=1;kind='process.start';data=@{}},
  @{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=2;kind='hot.log';data=@{code='CLIENT_VIEW';message='{"view":-11,"presenter":12}'}},
  @{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=3;kind='launch.result';data=@{ready=$true;restartRequired=$false}},
  @{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=4;kind='ui.stable';data=@{stableFrames=2;snapshot=@{view=-11;input=@{id=-12}}}}
 )
 foreach($item in $items){$item.utc='2026-10-31T00:00:00Z'}
 return $items
}
Check 'ready without stable actual login View remains incomplete' {$e=Events;Require ($null -eq (Get-FrameworkWindowsCompletion @($e[0..2]) (Owner) ('a'*32))) 'Ready terminated before actual stable login UI.'}
Check 'unrelated bootstrap stable marker cannot complete login' {$e=Events;$e[3].data.snapshot.view=0;Require ($null -eq (Get-FrameworkWindowsCompletion $e (Owner) ('a'*32))) 'Bootstrap stability substituted for the live login View.'}
Check 'one unchanged frame is insufficient' {$e=Events;$e[3].data.stableFrames=1;Require ($null -eq (Get-FrameworkWindowsCompletion $e (Owner) ('a'*32))) 'One stable frame substituted for two.'}
Check 'two stable frames on exact live View permit normal cleanup' {$point=Get-FrameworkWindowsCompletion (Events) (Owner) ('a'*32);Require ($point.kind -ceq 'launch.result' -and $point.data.ready -eq $true) 'Actual ready/stable result lost.'}
Check 'failed launch permits diagnostic cleanup without fake stable UI' {$e=Events;$e[2].data.ready=$false;$e[2].data.restartRequired=$true;Require ((Get-FrameworkWindowsCompletion @($e[0..2]) (Owner) ('a'*32)).data.ready -eq $false) 'Failure was fabricated as Ready.'}
Check 'host failure preserves original detail' {$e=Events;$e[2].kind='host.failure';$e[2].data=@{error='actual failure'};Require ((Get-FrameworkWindowsCompletion @($e[0..2]) (Owner) ('a'*32)).data.error -ceq 'actual failure') 'Original host failure lost.'}
Check 'foreign stable event rejects before cleanup' {$e=Events;$e[3].processId=124;$caught=$false;try{Get-FrameworkWindowsCompletion $e (Owner) ('a'*32)|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign process stability accepted.'}
Check 'unscoped normal Windows stop rejects before effects' {$caught=$false;try{Complete-FrameworkWindowsAttempt -Owned @{} -Completion @{} -OutputDirectory (Join-Path $root 'forbidden')}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_PREPARATION_GATE*'};Require ($caught -and -not(Test-Path (Join-Path $root 'forbidden'))) 'Unscoped stop caused effects.'}
Check 'full native events can be captured while native-style writer remains open' {
 $path=Join-Path $root 'live-native.jsonl';$writer=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
 try{
  $lines=@(Events|ForEach-Object {$_|ConvertTo-Json -Depth 12 -Compress});$bytes=[Text.Encoding]::UTF8.GetBytes(($lines -join "`n")+"`n");$writer.Write($bytes,0,$bytes.Length);$writer.Flush($true)
  $copy=Join-Path $root 'live-copy.jsonl';Copy-FrameworkLiveEventSnapshot $path $copy
  $captured=Read-FrameworkEvents $copy
  Require ((Get-FrameworkWindowsCompletion $captured (Owner) ('a'*32)).data.ready -eq $true) 'Live full-stream capture lost stable readiness.'
 }finally{$writer.Dispose()}
}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_WINDOWS_COMPLETION_RECEIPT $root";if($failed.Count){throw "D2_WINDOWS_COMPLETION_FAILED $($failed.Count)/$($records.Count)"}
