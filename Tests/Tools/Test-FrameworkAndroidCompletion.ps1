#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
$root=Join-Path $project ('Builds/Tests/D2/AndroidCompletion-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_COMPLETION_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_COMPLETION_FAIL $Name $($_.Exception.Message)"}}
function Owner {return @{runId='run';caseId='case';processId=123}}
function Events {
    return @(@{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=1;kind='process.start';data=@{}},@{runId='run';caseId='case';processId=123;attemptId=('a'*32);sequence=2;kind='launch.result';data=@{ready=$true;restartRequired=$false}})
}
Check 'normal readiness binds exact owned event stream' {$point=Get-FrameworkNativeCompletion (Events) (Owner) ('a'*32);Require ($point.kind -ceq 'launch.result' -and $point.data.ready) 'Original native result was lost.'}
Check 'failure completion permits diagnostic cleanup without claiming readiness' {$events=Events;$events[1].kind='host.failure';$events[1].data=@{error='Native setup failed'};$point=Get-FrameworkNativeCompletion $events (Owner) ('a'*32);Require ($point.kind -ceq 'host.failure' -and -not $point.data.ready) 'Failure was fabricated as Ready.'}
Check 'incomplete startup returns no terminal result' {$events=Events;Require ($null -eq (Get-FrameworkNativeCompletion @($events[0]) (Owner) ('a'*32))) 'A start marker was treated as completion.'}
foreach($field in @('runId','caseId','processId','attemptId','sequence')){Check "foreign or reordered completion rejects $field" {$events=Events;$events[1][$field]=if($field -eq 'processId'){124}elseif($field -eq 'sequence'){3}else{'foreign'};$caught=$false;try{Get-FrameworkNativeCompletion $events (Owner) ('a'*32)|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign/reordered native result was accepted.'}}
Check 'duplicate terminal results reject' {$events=Events;$last=$events[1].Clone();$last.sequence=3;$events+=@($last);$caught=$false;try{Get-FrameworkNativeCompletion $events (Owner) ('a'*32)|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Multiple native terminal results were accepted.'}
Check 'completion flags must be actual booleans' {$events=Events;$events[1].data.ready='true';$caught=$false;try{Get-FrameworkNativeCompletion $events (Owner) ('a'*32)|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'String readiness was accepted.'}
Check 'unscoped normal Android stop rejects before effects' {$caught=$false;try{Complete-FrameworkAndroidAttempt -Tool @{} -Owned @{} -Completion @{} -Context @{} -OutputDirectory (Join-Path $root 'forbidden')}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_PREPARATION_GATE*'};Require ($caught -and -not(Test-Path (Join-Path $root 'forbidden'))) 'Unscoped normal stop caused effects.'}
Check 'absent PID returns no invented process' {Require ($null -eq (ConvertFrom-FrameworkAndroidPid '' 1)) 'Absence became a fabricated PID.'}
Check 'one observed PID parses exactly' {Require ((ConvertFrom-FrameworkAndroidPid "123`n" 0) -eq 123) 'Exact PID was lost.'}
Check 'multiple malformed or failed PID responses reject' {foreach($sample in @(@('123 124',0),@('not-a-pid',0),@('123',1),@('',2))){$caught=$false;try{ConvertFrom-FrameworkAndroidPid $sample[0] $sample[1]|Out-Null}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Invalid PID response was accepted.'}}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_COMPLETION_RECEIPT $root";if($failed.Count){throw "D2_COMPLETION_FAILED $($failed.Count)/$($records.Count)"}
