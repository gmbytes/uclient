#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$library=Join-Path $project 'Tool/Client/Framework-Evidence.ps1'
if(Test-Path -LiteralPath $library){. $library}
. (Join-Path $PSScriptRoot 'Framework-Fixtures.ps1')
$failures=[Collections.Generic.List[string]]::new()
$records=@()
$receiptRoot=Join-Path $project ('Builds/Tests/D2/Evaluator-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $receiptRoot|Out-Null
$checks=0
function Check([string]$Name,[scriptblock]$Action){
    $script:checks++
    try { & $Action; $script:records+=@{name=$Name;passed=$true}; Write-Host "D2_UNIT_PASS $Name" }
    catch { $script:failures.Add($Name+': '+$_.Exception.Message);$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message}; Write-Host "D2_UNIT_FAIL $Name $($_.Exception.Message)" }
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
    StringPid={param($a)$a.processId='123';$a.observedProcessId='123';$a.process.processId='123';foreach($event in $a.events){$event.processId='123'}}
    StringTerminal={param($a)$a.process.terminal='true'}
    FloatingSequence={param($a)$a.events[0].sequence=1.0}
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
    StringReady={param($a)($a.events|Where-Object kind -eq 'launch.result').data.ready='true'}
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
Check 'string executed flag is invalid and cannot promote a case' {
    $e=New-FrameworkFixtureContext;$e.cases=@(@{id='update-v2';executed='true';attempts=@()});$r=Get-FrameworkAcceptance $e
    Require (@($r.errors|Where-Object {$_ -like 'E_SCALAR*'}).Count -gt 0 -and -not $r.passed) 'String execution flag was accepted.'
}
Check 'unfinished preparation independently blocks native acceptance' {
    $e=New-FrameworkFixtureContext;$e.evidenceKind='Native';$r=Get-FrameworkAcceptance $e
    Require (@($r.errors|Where-Object {$_ -like 'E_PREPARATION_GATE*'}).Count -eq 1 -and -not $r.passed) 'Completing one fixture could enable an unfinished native evaluator.'
}
Check 'Delivery throw binds exact cause selection and new-process K' {
    $pair=New-FrameworkFixtureThrow;Assert-FrameworkThrow $pair.case $pair.evidence
}
$throwNegatives=[ordered]@{
    MissingMarker={param($c)$c.attempts[0].events=@($c.attempts[0].events|Where-Object {$_.data.code -ne 'CLIENT_DELIVERY_THROW'})}
    LegacyMarker={param($c)($c.attempts[0].events|Where-Object {$_.data.code -eq 'CLIENT_DELIVERY_THROW'}).data.code='F8_THROW'}
    WrongTiming={param($c)($c.attempts[0].events|Where-Object {$_.data.code -eq 'CLIENT_DELIVERY_THROW'}).data.message='after Ready'}
    WrongCause={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.failures[0].error='System.InvalidOperationException: unrelated'}
    WrongOrigin={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.failures[0].error='System.InvalidOperationException: D2 Delivery ThrowOnStart`n at Client.Game.Entry.InitializeAsync'}
    StaleEpoch={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.failures[0].Epoch=999}
    WrongRelease={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.failures[0].ReleaseId='v2'}
    FakeReady={param($c)($c.attempts[0].events|Where-Object kind -eq 'launch.result').data.ready=$true}
    SameProcess={param($c)$c.attempts[1].processId=$c.attempts[0].processId}
}
foreach($name in $throwNegatives.Keys){$mutation=$throwNegatives[$name];Check "Delivery throw rejects $name" {$pair=New-FrameworkFixtureThrow;&$mutation $pair.case;$rejected=$false;try{Assert-FrameworkThrow $pair.case $pair.evidence}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Invalid Delivery throw proof was accepted.'}}
foreach($uiAction in @('rotation','keyboard','keyboard-rotation','background','safe-area','scroll','inflight-rotation','window-drag')){Check "native UI action shape $uiAction validates" {Assert-FrameworkUi (New-FrameworkFixtureUi $uiAction) @{action=$uiAction}}}
$actionNegatives=@(
    @{action='rotation';name='missing right orientation';mutate={param($c)foreach($event in $c.attempts[0].events|Where-Object {$_.kind -like 'ui.*'}){if($event.data.snapshot.orientation -eq 'LandscapeRight'){$event.data.snapshot.orientation='LandscapeLeft'}}}},
    @{action='keyboard';name='no actual IME';mutate={param($c)foreach($event in $c.attempts[0].events|Where-Object {$_.kind -like 'ui.*'}){$event.data.snapshot.keyboardVisible=$false}}},
    @{action='background';name='no pause';mutate={param($c)$c.attempts[0].events=@($c.attempts[0].events|Where-Object kind -ne 'application.pause')}},
    @{action='scroll';name='no scroll travel';mutate={param($c)foreach($event in $c.attempts[0].events|Where-Object {$_.kind -like 'ui.*'}){$event.data.snapshot.scrolls[0].y=1}}},
    @{action='inflight-rotation';name='duplicate request';mutate={param($c)Add-FrameworkFixtureEvent $c.attempts[0] 'session.request' @{request=2}}},
    @{action='window-drag';name='lost input';mutate={param($c)($c.attempts[0].events|Where-Object kind -eq 'ui.stable').data.snapshot.input.textSha256='0'*64}}
)
foreach($negative in $actionNegatives){Check ("native UI rejects "+$negative.name) {$c=New-FrameworkFixtureUi $negative.action;&$negative.mutate $c;$rejected=$false;try{Assert-FrameworkUi $c @{action=$negative.action}}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Invalid native action evidence was accepted.'}}
foreach($outcome in @('Rejected','Disconnected')){Check "native Tok $outcome preserves patch health" {$pair=New-FrameworkFixtureSession $outcome;$r=Get-FrameworkAcceptance $pair.evidence;Require (@($r.cases|Where-Object {$_.caseId -eq $pair.case.id -and $_.status -eq 'UnitPass'}).Count -eq 1) 'Native Tok-shaped case did not validate.'}}
$sessionNegatives=[ordered]@{
    SimulatedView={param($c)($c.attempts[0].events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":5,"presenter":6,"version":"v2","mode":"LocalSimulation"}'}
    NoAuthenticating={param($c)$c.attempts[0].events=@($c.attempts[0].events|Where-Object {$_.data.state -ne 'Authenticating'})}
    MissingServerBinary={param($c)$c.server.binarySha256=$null}
    WrongServerError={param($c)($c.attempts[0].events|Where-Object kind -eq 'session.result').data.ServerErrorCode=7}
}
foreach($name in $sessionNegatives.Keys){$mutate=$sessionNegatives[$name];Check "native Tok rejects $name" {$pair=New-FrameworkFixtureSession;&$mutate $pair.case;for($i=0;$i -lt $pair.case.attempts[0].events.Count;$i++){$pair.case.attempts[0].events[$i].sequence=$i+1};$r=Get-FrameworkAcceptance $pair.evidence;Require (@($r.cases|Where-Object {$_.caseId -eq $pair.case.id -and $_.status -eq 'Fail'}).Count -eq 1) 'Invalid Tok session was accepted.'}}
Check 'Android translated ARM64 environment stays explicit' {Assert-FrameworkContext (New-FrameworkFixtureAndroid)}
$androidNegatives=[ordered]@{
    WrongDevice={param($e)$e.environment.deviceId='other'}
    WrongInstalledAbi={param($e)$e.environment.installedPrimaryAbi='x86_64'}
    MissingTranslation={param($e)$e.environment.translation=''}
    PhysicalArmClaim={param($e)$e.environment.physicalArm='Pass'}
    Reinstallation={param($e)$e.environment.installCount=2}
    ChangedApk={param($e)$e.environment.installedApkAfterSha256='0'*64}
    ChangedNativeLibrary={param($e)$e.environment.installedNativeLibraries['lib/arm64-v8a/libil2cpp.so']='0'*64}
}
foreach($name in $androidNegatives.Keys){$mutate=$androidNegatives[$name];Check "Android rejects $name" {$e=New-FrameworkFixtureAndroid;&$mutate $e;$rejected=$false;try{Assert-FrameworkContext $e}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Incomplete or changed Android package/environment was accepted.'}}
$receipt=@{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failures.Count -eq 0);count=$checks;cases=$records;sourceHashes=@{evaluator=(Get-FileHash -LiteralPath $library).Hash.ToLowerInvariant();suite=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()}}
$receipt|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $receiptRoot 'test-result.json') -Encoding utf8NoBOM
Write-Host "D2_UNIT_RECEIPT $receiptRoot"
if($failures.Count){throw "D2_UNIT_FAILED $($failures.Count)/$checks`n$($failures -join "`n")"}
Write-Host "D2_UNIT_SUITE_PASS $checks (synthetic unit evidence only; native acceptance NotRun)"
