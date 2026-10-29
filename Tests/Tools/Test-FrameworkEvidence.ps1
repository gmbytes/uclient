#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$library=Join-Path $project 'Tool/Client/Framework-Evidence.ps1'
if(Test-Path -LiteralPath $library){. $library}
. (Join-Path $PSScriptRoot 'Framework-Fixtures.ps1')
$failures=[Collections.Generic.List[string]]::new()
$checks=0
function Check([string]$Name,[scriptblock]$Action){
    $script:checks++
    try { & $Action; Write-Host "D2_UNIT_PASS $Name" }
    catch { $script:failures.Add($Name+': '+$_.Exception.Message); Write-Host "D2_UNIT_FAIL $Name $($_.Exception.Message)" }
}
function Require($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Rejects([hashtable]$Evidence,[string]$Code){
    if(-not(Get-Command Get-FrameworkAcceptance -ErrorAction SilentlyContinue)){throw 'Incomplete evidence has no fail-closed evaluator.'}
    $result=Get-FrameworkAcceptance $Evidence
    Require (-not $result.criteriaAccepted) 'Incomplete or incorrect evidence was accepted.'
    Require (@($result.errors | Where-Object {$_ -like "$Code*"}).Count -gt 0) "Expected diagnostic $Code; got $($result.errors -join '; ')"
}
Check 'missing platform is rejected' { Rejects @{runId='unit';cases=@()} 'E_PLATFORM' }
Check 'Android without selected device is rejected' { Rejects @{runId='unit';platform='Android';cases=@()} 'E_DEVICE' }
Check 'required cases cannot disappear' {
    if(-not(Get-Command Get-FrameworkAcceptance -ErrorAction SilentlyContinue)){throw 'Required cases have no evaluator.'}
    $r=Get-FrameworkAcceptance @{runId='unit';platform='Windows64';cases=@()}
    Require (-not $r.criteriaAccepted) 'No cases was accepted.'
    Require (@($r.cases|Where-Object status -eq NotRun).Count -ge 34) 'Missing required cases must be explicit NotRun.'
}
Check 'complete native-shaped unit attempt validates' {Assert-FrameworkAttempt (New-FrameworkFixtureAttempt) (New-FrameworkFixtureContext) 'update-v2'}
$attemptNegatives=[ordered]@{
    StaleRun={param($a)$a.events[0].runId='old'}
    StaleAttempt={param($a)$a.events[0].attemptId='0'*32}
    WrongPid={param($a)$a.observedProcessId=999}
    WrongIdentity={param($a)$a.events[1].data.player.PlayerBuildId='wrong'}
    NativeEditor={param($a)$a.events[1].data.platform='WindowsEditor'}
    MissingAttemptId={param($a)$a.attemptId=''}
    ReorderedEvents={param($a)$a.events[2].sequence=1}
    UnownedProcess={param($a)$a.process.playerBuildId='other'}
}
foreach($name in $attemptNegatives.Keys){$mutate=$attemptNegatives[$name];Check "attempt rejects $name" {$a=New-FrameworkFixtureAttempt;& $mutate $a;$rejected=$false;try{Assert-FrameworkAttempt $a (New-FrameworkFixtureContext) 'update-v2'}catch{$rejected=$true};Require $rejected 'Invalid native attempt was accepted.'}}
Check 'ready proves actual branch Parser resource metadata and UI' {Assert-FrameworkReady (New-FrameworkFixtureAttempt) (New-FrameworkFixtureContext) 'v2'}
Check 'negative Unity identities are compared exactly and accepted' {
    $a=New-FrameworkFixtureAttempt
    ($a.events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":-5,"presenter":-6,"version":"v2","mode":"LocalSimulation"}'
    foreach($event in $a.events|Where-Object {$_.kind -in @('ui.sample','ui.stable')}){$event.data.snapshot.view=-5;$event.data.snapshot.input.id=-10}
    Assert-FrameworkReady $a (New-FrameworkFixtureContext) 'v2'
}
$readyNegatives=[ordered]@{
    MissingBranch={param($a)$a.events=@($a.events|Where-Object {$_.data.code -ne 'CLIENT_CODE_VERSION'})}
    MissingNewParser={param($a)$a.events=@($a.events|Where-Object {$_.data.code -ne 'CLIENT_PARSER_EXECUTED'})}
    OldParser={param($a)($a.events|Where-Object {$_.data.code -eq 'CLIENT_PARSER_EXECUTED'}).data.message='BaselineMessage list,map,unknown-fields,uint64,hot-generics,empty-registry'}
    MissingMetadata={param($a)$a.events=@($a.events|Where-Object kind -ne metadata)}
    FailedMetadata={param($a)($a.events|Where-Object kind -eq metadata)[0].data.message='F6_METADATA player mscorlib.dll BAD_IMAGE'}
    MissingReady={param($a)($a.events|Where-Object kind -eq 'launch.result').data.ready=$false}
    WrongResource={param($a)($a.events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":5,"presenter":6,"version":"v1","mode":"LocalSimulation"}'}
    NonIntegerView={param($a)($a.events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":"foo","presenter":6,"version":"v2","mode":"LocalSimulation"}';foreach($event in $a.events|Where-Object {$_.kind -in @('ui.sample','ui.stable')}){$event.data.snapshot.view='foo'}}
    WrongRoot={param($a)$a.slotInventory.root='C:/other'}
    WrongDll={param($a)$a.slotInventory.dllSha256='0'*64}
    WrongTable={param($a)$a.slotInventory.tableSha256='0'*64}
    MissingUnsafeFullName={param($a)$a.slotInventory.metadata.Remove('System.Runtime.CompilerServices.Unsafe.dll')}
    ChangedOriginalMetadata={param($a)$a.slotInventory.metadata['mscorlib.dll']='0'*64}
    NoActualUi={param($a)$a.events=@($a.events|Where-Object kind -ne 'ui.stable')}
    DoubleRoot={param($a)($a.events|Where-Object kind -eq 'launch.result').data.resourceAttempts=2}
}
foreach($name in $readyNegatives.Keys){$mutate=$readyNegatives[$name];Check "ready rejects $name" {$a=New-FrameworkFixtureAttempt;& $mutate $a;$rejected=$false;try{Assert-FrameworkReady $a (New-FrameworkFixtureContext) 'v2'}catch{$rejected=$true};Require $rejected 'Missing actual startup evidence was accepted.'}}
Check 'native package cannot change before or after execution' {
    foreach($field in @('nativeBefore','nativeAfter')){$c=New-FrameworkFixtureContext;$c[$field]['GameAssembly.dll']='0'*64;$rejected=$false;try{Assert-FrameworkContext $c}catch{$rejected=$true};Require $rejected 'Changed native inventory was accepted.'}
}
Check 'Android cannot omit actual ABI translation and installed APK evidence' {
    $e=New-FrameworkFixtureContext;$e.platform='Android';$e.deviceId='emulator-5554';$e.identity.Platform='Android';$e.identity.Architecture='arm64'
    $rejected=$false;try{Assert-FrameworkContext $e}catch{$rejected=$true};Require $rejected 'Android environment omission was accepted.'
}
Check 'actual partial kill and separate K recovery shape validates' {Assert-FrameworkKill (New-FrameworkFixtureCase) (New-FrameworkFixtureContext) @{point='download.partial'}}
$killNegatives=[ordered]@{
    ZeroTransferred={param($c)$c.attempts[0].checkpoint.data.bytes=0}
    CompletedTransfer={param($c)$c.attempts[0].checkpoint.data.bytes=10}
    NoAuthenticatedLength={param($c)$c.attempts[0].checkpoint.data.expectedBytes=$null}
    WrongKillPid={param($c)$c.kill.processId=999}
    SameRecoveryPid={param($c)$c.attempts[1].processId=123}
    SameAttempt={param($c)$c.attempts[1].attemptId=$c.attempts[0].attemptId}
    LostOldBytes={param($c)$c.preservation.after['v1/file']='0'*64}
    SparseOldBytes={param($c)$c.preservation.before=@{'unrelated/file'=('c'*64)};$c.preservation.after=@{'unrelated/file'=('c'*64)}}
    MissingCheckpoint={param($c)$c.attempts[0].checkpoint=$null}
    WrongBoundary={param($c)$c.attempts[0].checkpoint.point='rename.before'}
    NoActualKill={param($c)$c.kill.action='IOException'}
}
foreach($name in $killNegatives.Keys){$mutate=$killNegatives[$name];Check "kill rejects $name" {$c=New-FrameworkFixtureCase;& $mutate $c;$rejected=$false;try{Assert-FrameworkKill $c (New-FrameworkFixtureContext) @{point='download.partial'}}catch{$rejected=$true};Require $rejected 'Invalid strong-kill proof was accepted.'}}
Check 'UI checks actual size and stable frames' {
    $c=New-FrameworkFixtureCase;$c.attempts=@(New-FrameworkFixtureAttempt);Assert-FrameworkUi $c @{action='resize';width=720;height=1280}
}
$uiNegatives=[ordered]@{
    NoOperator={param($c)$c.operator.name=''}
    NoScreenshot={param($c)$c.operator.screenshots=@()}
    MissingActionInterval={param($c)$c.operator.firstSequence=$null}
    WrongSize={param($c)@($c.attempts[0].events|Where-Object kind -eq 'ui.stable')[0].data.snapshot.width=1080}
    SlowConvergence={param($c)@($c.attempts[0].events|Where-Object kind -eq 'ui.stable')[0].data.frame=15}
    OutsideSafeArea={param($c)@($c.attempts[0].events|Where-Object kind -eq 'ui.stable')[0].data.snapshot.buttons[0].bounds.right=900}
}
foreach($name in $uiNegatives.Keys){$mutate=$uiNegatives[$name];Check "UI rejects $name" {$c=New-FrameworkFixtureCase;$c.attempts=@(New-FrameworkFixtureAttempt);&$mutate $c;$rejected=$false;try{Assert-FrameworkUi $c @{action='resize';width=720;height=1280}}catch{$rejected=$true};Require $rejected 'Invalid actual UI proof was accepted.'}}
Check 'tampered payload rejects C while next PID loads K' {Assert-FrameworkFault (New-FrameworkFixtureFault) (New-FrameworkFixtureContext) @{id='bad-hash';kind='preparation-reject';failurePattern='hash|SHA256'}}
Check 'authenticated GF table failure requires new process K' {Assert-FrameworkFault (New-FrameworkFixtureFault -Semantic) (New-FrameworkFixtureContext) @{id='semantic-version-table';kind='semantic-reject';failurePattern='GameFramework|Deserialize|version|Package'}}
$faultNegatives=[ordered]@{
    NoGfError={param($c)$c.attempts[0].events=@($c.attempts[0].events|Where-Object kind -ne 'native.error')}
    TimeoutOnly={param($c)($c.attempts[0].events|Where-Object kind -eq 'native.error').data.message='Local ready timeout'}
    HotLoaded={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.hotAssemblies=@('Client.Game')}
    MetadataLoaded={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.metadataCount=1}
    Promoted={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.state.KnownGood='v2'}
    NoRestart={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.restartRequired=$false}
    SameProcessFallback={param($c)$c.attempts[1].processId=123}
    SecondRoot={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.resourceAttempts=2}
    NoAuthentication={param($c)$c.fault.authenticated=$false}
}
foreach($name in $faultNegatives.Keys){$mutate=$faultNegatives[$name];Check "semantic table rejects $name" {$c=New-FrameworkFixtureFault -Semantic;&$mutate $c;$rejected=$false;try{Assert-FrameworkFault $c (New-FrameworkFixtureContext) @{id='semantic-version-table';kind='semantic-reject';failurePattern='GameFramework|Deserialize|version|Package'}}catch{$rejected=$true};Require $rejected 'Invalid GF table rejection was accepted.'}}
Check 'aggregate evaluates executed cases and preserves all missing cases' {
    $e=New-FrameworkFixtureContext;$a=New-FrameworkFixtureAttempt 'restart-v2';$e.cases=@(@{id='restart-v2';executed=$true;attempts=@($a)})
    $r=Get-FrameworkAcceptance $e
    Require (@($r.cases|Where-Object {$_.caseId -eq 'restart-v2' -and $_.status -eq 'Fail'}).Count -eq 1) 'Missing prior-process correlation must fail an executed restart case.'
    Require (-not $r.passed -and @($r.cases|Where-Object status -eq NotRun).Count -eq 33) 'Unexecuted required cases must stay NotRun.'
}
Check 'declared Pass without execution remains NotRun' {
    $e=New-FrameworkFixtureContext;$e.cases=@(@{id='update-v2';status='Pass';executed=$false});$r=Get-FrameworkAcceptance $e
    Require (@($r.cases|Where-Object {$_.caseId -eq 'update-v2' -and $_.status -eq 'NotRun'}).Count -eq 1 -and -not $r.passed) 'A supplied status must never be trusted.'
}
Check 'duplicate logical case evidence fails closed' {
    $e=New-FrameworkFixtureContext;$e.cases=@(@{id='update-v2';executed=$true},@{id='update-v2';executed=$true});$r=Get-FrameworkAcceptance $e
    Require (@($r.errors|Where-Object {$_ -like 'E_DUPLICATE_CASE*'}).Count -eq 1) 'Duplicate cases were ignored.'
}
if($failures.Count){throw "D2_UNIT_FAILED $($failures.Count)/$checks`n$($failures -join "`n")"}
Write-Host "D2_UNIT_SUITE_PASS $checks (synthetic unit evidence only; native acceptance NotRun)"
