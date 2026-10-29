[CmdletBinding()]param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$validator=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Tool/Client/Assert-HotUpdateEvidence.ps1'))
if(-not(Test-Path -LiteralPath $validator)){throw 'F8 evidence validator is missing: incomplete native evidence must be rejected.'}
. $validator
function Fixture {
    $cases=@()
    foreach($name in @('v1','v2','throw','fallback','offline')){
        $id=if($name-eq'v1'){'v1'}elseif($name-eq'throw'){'throw'}else{'v2'}
        $cases+=@{runId='run';caseId=$name;processId=100+$cases.Count;playerBuildId='player';releaseId=$id;manifestHash=if($id-eq'v1'){'a'*64}elseif($id-eq'v2'){'b'*64}else{'c'*64};ready=($name-ne'throw');restartRequired=($name-eq'throw');epoch=1;baselineReads=if($name-eq'v1'){3}else{0};metadata=@('F6_METADATA player mscorlib.dll OK');markers=@('F8_PROBES all','F8_ENTRY_RETURN');resourceVersion=if($id-eq'v1'){'F8 resource v1'}else{'F8 resource v2'};newType=($id-ne'v1');answer=if($id-eq'v1'){0}else{42};sameProcessReloadRejected=($name-eq'throw');network=@(@{success=($name-ne'offline');bytes=if($name-eq'offline'){0}else{256}});state=@{KnownGood=if($id-eq'v1'){'v1'}else{'v2'};BadReleaseIds=if($name-in@('throw','fallback','offline')){@('throw')}else{@()}}}
    }
    foreach($c in $cases){$c.observedProcessId=$c.processId;foreach($n in $c.network){$n.path='channel.json';$n.bytesKnown=$true};$c.downloadEvents=@(@{runId='run';processId=$c.processId;serialId=1;uri='http://127.0.0.1:9999/channel.json';path='scratch/channel.json';startCount=1;startLength=0;maxLength=if($c.caseId-eq'offline'){0}else{256};failed=($c.caseId-eq'offline');succeeded=($c.caseId-ne'offline');identityMismatch=$false})}
    $cases[2].markers=@('F8_PROBES all','F8_THROW after actual UI Ready request')
    $cases[2].failures=@(@{releaseId='throw';epoch=1;phase='Hotfix';code='HotfixInitializationFailed';error="System.InvalidOperationException: F8 ThrowOnStart`n  at Client.Game.HotUpdateProof.VersionBranch.AfterReady (Client.Contracts.IClientContext context)"})
    return @{schemaVersion=1;task='F8';runId='run';platform='Windows64';playerBuildId='player';archiveNative=@{exe='d'*64;native='e'*64};nativeBefore=@{exe='d'*64;native='e'*64};nativeAfter=@{exe='d'*64;native='e'*64};metadataNames=@('mscorlib.dll');aotNames=@('mscorlib');v1DllHash='1'*64;v2DllHash='2'*64;v1ResourceHash='3'*64;v2ResourceHash='4'*64;cases=$cases}
}
$valid=Fixture;Assert-HotUpdateEvidence $valid
$mutations=[ordered]@{
    MissingV1={param($r)$r.cases=$r.cases|Where-Object caseId -ne v1}
    NativeChanged={param($r)$r.nativeAfter.native='f'*64}
    NativeChangedBeforeExecution={param($r)$r.nativeBefore.native='f'*64;$r.nativeAfter.native='f'*64}
    WrongOwnedPid={param($r)$r.cases[0].observedProcessId=999}
    TextOnly={param($r)$r.v2DllHash=$r.v1DllHash}
    NoNewBranch={param($r)$r.cases[1].answer=0}
    HotDllInAot={param($r)$r.aotNames+= 'Client.Game'}
    MetadataFailed={param($r)$r.cases[1].metadata=@('F6_METADATA player mscorlib.dll BAD_IMAGE')}
    MissingDevice={param($r)$r.platform='Android';$r.deviceId=''}
    StaleRun={param($r)$r.cases[1].runId='old'}
    NoNetworkAttempt={param($r)$r.cases[4].network=@()}
    SameProcess={param($r)$r.cases[1].processId=$r.cases[0].processId}
    NoDownloadEvents={param($r)$r.cases[4].downloadEvents=@()}
    NoFreshStart={param($r)$r.cases[4].downloadEvents[0].startCount=0}
    ReceivedBytes={param($r)$r.cases[4].downloadEvents[0].maxLength=1}
    UnknownLengthNoEvents={param($r)$r.cases[4].network[0].bytesKnown=$false;$r.cases[4].downloadEvents=@()}
    MissingThrowMarker={param($r)$r.cases[2].markers=@('F8_PROBES all')}
    WrongThrowMarker={param($r)$r.cases[2].markers=@('F8_PROBES all','F8_THROW before actual UI Ready request')}
    MissingThrowFailure={param($r)$r.cases[2].failures=@()}
    WrongThrowException={param($r)$r.cases[2].failures[0].error=$r.cases[2].failures[0].error.Replace('System.InvalidOperationException','System.IO.IOException')}
    WrongThrowCause={param($r)$r.cases[2].failures[0].error=$r.cases[2].failures[0].error.Replace('F8 ThrowOnStart','F8 ThrowOnStart: unrelated failure')}
    WrongThrowOrigin={param($r)$r.cases[2].failures[0].error=$r.cases[2].failures[0].error.Replace('VersionBranch.AfterReady','UILogin.Show')}
    UnrelatedStartupFailure={param($r)$r.cases[2].failures=@(@{releaseId='throw';epoch=1;phase='Resources';code='ResourceInitializationFailed';error='System.InvalidOperationException: UI load failed'})}
    UnboundThrowFailure={param($r)$r.cases[2].failures[0].releaseId='v2'}
    StaleThrowFailure={param($r)$r.cases[2].failures[0].epoch=2}
    ContradictoryEntryReturn={param($r)$r.cases[2].markers+='F8_ENTRY_RETURN'}
}
$accepted=@()
foreach($name in $mutations.Keys){$r=Fixture;& $mutations[$name] $r;$rejected=$false;try{Assert-HotUpdateEvidence $r}catch{$rejected=$true};if(-not$rejected){$accepted+=$name;Write-Host "F8_NEGATIVE_ACCEPTED $name"}}
if($accepted.Count){throw "F8 validator accepted invalid evidence: $($accepted-join', ')"}
$unknown=Fixture;$unknown.cases[4].network[0].bytesKnown=$false;$unknown.cases[4].network[0].bytes=999;Assert-HotUpdateEvidence $unknown
function AndroidFixture {
    $r=Fixture;$r.platform='Android';$r.deviceId='emulator-5554';$r.deviceState='device';$r.installCount=1;$r.installedPrimaryAbi='arm64-v8a';$r.apkCertificateSha256='a'*64;$r.apkNativeHashes=@{native='b'*64};$r.apkHash='c'*64;$r.installedApkBeforeHash='c'*64;$r.installedApkAfterHash='c'*64
    foreach($c in $r.cases){$c.pointerBits=64;$c.androidProcess64Bit=$true;$c.observedProcessId=$c.processId};return $r
}
Assert-HotUpdateEvidence (AndroidFixture)
foreach($change in @({param($r)$r.installCount=2},{param($r)$r.installedApkAfterHash='e'*64},{param($r)$r.apkCertificateSha256=''},{param($r)$r.cases[1].observedProcessId=999},{param($r)$r.cases[1].androidProcess64Bit=$false})){
    $r=AndroidFixture;&$change $r;$rejected=$false;try{Assert-HotUpdateEvidence $r}catch{$rejected=$true};if(-not$rejected){throw 'Android installed/native/process evidence was accepted without its required facts.'}
}
Write-Host "F8_EVIDENCE_TEST_PASS $($mutations.Count+8)"
