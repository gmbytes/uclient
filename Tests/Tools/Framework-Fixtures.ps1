# Synthetic records matching NativeEvidence/NativeClientHost at the frozen D1 contract.
# These records never establish actual native acceptance.
function New-FrameworkFixtureContext {
    return @{schemaVersion=1;evidenceKind='SyntheticUnit';runId='d2unit';platform='Windows64';playerBuildId='player';profilePackageId='com.sgame.client';updateEndpoint='http://127.0.0.1/';
        identity=@{PlayerBuildId='player';Platform='StandaloneWindows64';Architecture='x64';UnityVersion='2022.3.25f1';ContractVersion='1';EnvelopeSha256=('e'*64);AotMetadataSetId=('f'*64)};
        archiveNative=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};nativeBefore=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};nativeAfter=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};
        metadataNames=@('mscorlib.dll','System.Runtime.CompilerServices.Unsafe.dll');metadataHashes=@{'mscorlib.dll'=('7'*64);'System.Runtime.CompilerServices.Unsafe.dll'=('8'*64)};
        releases=@{v1=@{manifestSha256=('1'*64);dllSha256=('2'*64);protocolDllSha256=('9'*64);tableSha256=('3'*64);fileLengths=@{'Hotfix/Client.Game.dll'=10};files=@{file=('c'*64);'Hotfix/Client.Game.dll'=('2'*64);'Hotfix/Client.Game.Protocol.dll'=('9'*64);'Resources/GameFrameworkVersion.dat'=('3'*64)}};v2=@{manifestSha256=('4'*64);dllSha256=('5'*64);protocolDllSha256=('8'*64);tableSha256=('6'*64);fileLengths=@{'Hotfix/Client.Game.dll'=10};files=@{file=('d'*64);'Hotfix/Client.Game.dll'=('5'*64);'Hotfix/Client.Game.Protocol.dll'=('8'*64);'Resources/GameFrameworkVersion.dat'=('6'*64)}}};cases=@()}
}
function Add-FrameworkFixtureEvent($Attempt,[string]$Kind,$Data) {
    $Attempt.events+=@{runId='d2unit';caseId=$Attempt.caseId;attemptId=$Attempt.attemptId;processId=$Attempt.processId;sequence=($Attempt.events.Count+1);utc='2026-10-30T00:00:01Z';kind=$Kind;data=$Data}
}
function New-FrameworkFixtureAttempt([string]$CaseId='update-v2',[string]$Version='v2',[int]$ProcessId=123) {
    $context=New-FrameworkFixtureContext
    $selection=@{ReleaseId=$Version;Epoch=1;ManifestSha256=$context.releases[$Version].manifestSha256;Root="C:/native/Releases/player/slots/$Version"}
    $state=@{KnownGood=$Version;PreviousGood=if($Version -eq 'v1'){$null}else{'v1'};Candidate=$null;Attempt=0;BadReleaseIds=@()}
    $a=@{caseId=$CaseId;attemptId=([Guid]::NewGuid().ToString('N'));processId=$ProcessId;observedProcessId=$ProcessId;events=@();process=@{runId='d2unit';caseId=$CaseId;processId=$ProcessId;playerBuildId='player';startedUtc='2026-10-30T00:00:00Z';finishedUtc='2026-10-30T00:02:00Z';terminal=$true};evidencePaths=@('synthetic/events.jsonl');slotInventory=@{releaseId=$Version;manifestSha256=$selection.ManifestSha256;root=$selection.Root;dllSha256=$context.releases[$Version].dllSha256;protocolDllSha256=$context.releases[$Version].protocolDllSha256;tableSha256=$context.releases[$Version].tableSha256;metadata=@{'mscorlib.dll'=('7'*64);'System.Runtime.CompilerServices.Unsafe.dll'=('8'*64)}}}
    Add-FrameworkFixtureEvent $a 'process.start' @{utc='2026-10-30T00:00:01Z'}
    Add-FrameworkFixtureEvent $a 'native.runtime' @{applicationId='';profilePackageId='com.sgame.client';platform='WindowsPlayer';unityVersion='2022.3.25f1';pointerBits=64}
    Add-FrameworkFixtureEvent $a 'native.identity' @{player=$context.identity;platform='WindowsPlayer';pointerBits=64;profileSha256=('9'*64);releaseRoot='C:/native/Releases/player';applicationId='';profilePackageId='com.sgame.client'}
    Add-FrameworkFixtureEvent $a 'state.before' $state
    foreach($phase in @('Select','ResourceInit','Metadata')){Add-FrameworkFixtureEvent $a 'release.phase' @{phase=$phase;selection=$selection;resourceAttempts=1;metadataCount=0;frame=1}}
    foreach($name in $context.metadataNames){Add-FrameworkFixtureEvent $a 'metadata' @{message="F6_METADATA player $name OK";selection=$selection}}
    Add-FrameworkFixtureEvent $a 'release.phase' @{phase='Hotfix';selection=$selection;resourceAttempts=1;metadataCount=2;frame=1}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_ENTRY_EXECUTING';message='Client.Game.Entry'}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_VIEW';message=(@{view=5;presenter=6;version=("Framework "+$Version);mode='LocalSimulation'}|ConvertTo-Json -Compress)}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_CODE_VERSION';message=$Version}
    $parser=if($Version -eq 'v1'){'BaselineMessage'}else{'AddedMessage'}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_PARSER_EXECUTED';message="$parser list,map,unknown-fields,uint64,hot-generics,empty-registry"}
    foreach($phase in @('LoginReady','Ready')){Add-FrameworkFixtureEvent $a 'release.phase' @{phase=$phase;selection=$selection;resourceAttempts=1;metadataCount=2;frame=1}}
    Add-FrameworkFixtureEvent $a 'launch.result' @{ready=$true;restartRequired=$false;selection=$selection;state=$state;metadataCount=2;resourceAttempts=1;hotAssemblies=@('Client.Game.Protocol','Client.Game');failures=@();network=@()}
    $snapshot=@{width=720;height=1280;safeArea=@{x=0;y=0;width=720;height=1280};orientation='Portrait';keyboardVisible=$false;keyboardArea=@{x=0;y=0;width=0;height=0};selected=10;view=5;input=@{id=10;textSha256=('c'*64);length=4;focused=$true;caret=4;selectionAnchor=4;selectionFocus=4;bounds=@{left=10;right=710;bottom=700;top=780}};layout=@{Width=720;Height=1280;Revision=1;mode='Portrait';safeArea=@{x=0;y=0;width=720;height=1280}};buttons=@(@{id=11;name='Submit';active=$true;interactable=$true;bounds=@{left=10;right=710;bottom=500;top=580}});scrolls=@(@{id=12;x=0;y=1;bounds=@{left=0;right=720;bottom=0;top=1280}})}
    Add-FrameworkFixtureEvent $a 'ui.sample' @{frame=10;realtime=1;snapshot=$snapshot}
    Add-FrameworkFixtureEvent $a 'ui.stable' @{frame=12;realtime=1.04;stableFrames=2;snapshot=$snapshot}
    return $a
}
function New-FrameworkFixtureCase([string]$Kind='strong-kill') {
    $first=New-FrameworkFixtureAttempt 'kill-download-partial' 'v1' 123
    $recovery=New-FrameworkFixtureAttempt 'kill-download-partial' 'v1' 124
    $first.events=@($first.events|Select-Object -First 4)
    $data=@{path='C:/native/staging/hash.partial/downloads/0.partial';uri='http://127.0.0.1/Hotfix/Client.Game.dll';bytes=5;expectedBytes=10;serial=2}
    Add-FrameworkFixtureEvent $first 'preparation' @{releaseId='v2';manifestSha256=('4'*64);destination='C:/native/Releases/player/slots/v2'}
    Add-FrameworkFixtureEvent $first 'download.partial' $data
    $first.checkpoint=@{runId='d2unit';caseId=$first.caseId;attemptId=$first.attemptId;processId=123;point='download.partial';purpose=$null;token="d2unit:$($first.caseId):$($first.attemptId):123:download.partial:";data=$data}
    return @{id=$first.caseId;executed=$true;attempts=@($first,$recovery);knownGood='v1';candidate='v2';preservation=(New-FrameworkFixturePreservation @('v1'));transfer=(New-FrameworkFixtureTransfer);server=@{runId='d2unit';processId=321;endpoint='http://127.0.0.1/';binarySha256=('f'*64);kind='ReleaseServer';evidencePaths=@('synthetic/server.json')};kill=@{processId=123;attemptId=$first.attemptId;point='download.partial';utc='2026-10-30T00:01:00Z';action='TerminateProcess';terminal=$true};operator=@{name='unit';utc='2026-10-30T00:00:01Z';environment='synthetic';action='resize';firstSequence=18;lastSequence=19;screenshots=@(@{path='synthetic/screen.png';sha256=('d'*64)})}}
}
function New-FrameworkFixtureFault([switch]$Semantic) {
    $id=if($Semantic){'semantic-version-table'}else{'bad-hash'}
    $c=New-FrameworkFixtureCase;$c.id=$id
    $first=New-FrameworkFixtureAttempt $id 'v2' 123
    $first.events=@($first.events|Where-Object {$_.kind -in @('process.start','native.runtime','native.identity','state.before') -or ($_.kind -eq 'release.phase' -and $_.data.phase -eq 'ResourceInit' -and $Semantic)})
    $state=@{KnownGood='v1';PreviousGood=$null;Candidate=$null;Attempt=0;BadReleaseIds=@('v2')}
    ($first.events|Where-Object kind -eq 'state.before').data=@{KnownGood='v1';PreviousGood=$null;Candidate=$null;Attempt=0;BadReleaseIds=@()}
    if($Semantic){Add-FrameworkFixtureEvent $first 'native.error' @{message='GameFramework version table deserialize failed';selection=@{ReleaseId='v2';ManifestSha256=('4'*64);Root='C:/native/Releases/player/slots/v2'}}}
    Add-FrameworkFixtureEvent $first 'launch.result' @{ready=$false;restartRequired=[bool]$Semantic;selection=@{ReleaseId='v2';Epoch=1;ManifestSha256=('4'*64);Root='C:/native/Releases/player/slots/v2'};state=$state;metadataCount=0;resourceAttempts=([int][bool]$Semantic);hotAssemblies=@();failures=@(@{ReleaseId='v2';Epoch=1;phase=if($Semantic){'ResourceInit'}else{'Prepare'};Code=if($Semantic){'ResourceInitializationFailed'}else{'PreparationFailed'};error=if($Semantic){'GameFramework version table deserialize failed'}else{'SHA256 hash mismatch'}})}
    for($i=0;$i -lt $first.events.Count;$i++){$first.events[$i].sequence=$i+1}
    $c.attempts=@($first,(New-FrameworkFixtureAttempt $id 'v1' 124))
    $c.fault=@{kind=$id;candidate='v2';manifestSha256=('4'*64);payloadSha256=('a'*64);tableSha256=('6'*64);inputPaths=@('synthetic/fault.json');authenticated=[bool]$Semantic}
    return $c
}
function New-FrameworkFixtureThrow {
    $e=New-FrameworkFixtureContext;$e.releases['throw']=@{manifestSha256=('a'*64);dllSha256=('b'*64);protocolDllSha256=('8'*64);tableSha256=('c'*64);files=@{file=('d'*64)}}
    $c=New-FrameworkFixtureFault -Semantic;$c.id='throw-on-start';$c.candidate='throw';$c.knownGood='v1';$c.fault=@{kind='ThrowOnStart';fixtureContractSha256='pending'}
    $a=New-FrameworkFixtureAttempt $c.id 'v2' 123
    ($a.events|Where-Object kind -eq 'state.before').data=@{KnownGood='v1';PreviousGood=$null;Candidate=$null;Attempt=0;BadReleaseIds=@()}
    ($a.events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":5,"presenter":6,"version":"Framework throw","mode":"LocalSimulation"}'
    foreach($event in $a.events){if($event.data.selection){$event.data.selection.ReleaseId='throw';$event.data.selection.ManifestSha256='a'*64;$event.data.selection.Root='C:/native/Releases/player/slots/throw'}}
    $a.events=@($a.events|Where-Object {$_.kind -notin @('launch.result','ui.sample','ui.stable') -and $_.data.phase -notin @('LoginReady','Ready')})
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_DELIVERY_THROW';message='after login first frame; before ReportReady'}
    Add-FrameworkFixtureEvent $a 'launch.result' @{ready=$false;restartRequired=$true;selection=@{ReleaseId='throw';Epoch=1;ManifestSha256=('a'*64);Root='C:/native/Releases/player/slots/throw'};state=@{KnownGood='v1';PreviousGood=$null;BadReleaseIds=@('throw')};metadataCount=2;resourceAttempts=1;hotAssemblies=@('Client.Game','Client.Game.Protocol');failures=@(@{ReleaseId='throw';Epoch=1;phase='Hotfix';Code='HotfixInitializationFailed';error="System.InvalidOperationException: D2 Delivery ThrowOnStart`n at Client.Game.DeliveryVersion.Run (Client.Contracts.ILogService log)"})}
    for($i=0;$i -lt $a.events.Count;$i++){$a.events[$i].sequence=$i+1}
    $a.slotInventory=@{releaseId='throw';manifestSha256=('a'*64);root='C:/native/Releases/player/slots/throw';dllSha256=('b'*64);protocolDllSha256=('8'*64);tableSha256=('c'*64);metadata=$e.metadataHashes}
    $c.attempts=@($a,(New-FrameworkFixtureAttempt $c.id 'v1' 124))
    return @{case=$c;evidence=$e}
}
function New-FrameworkFixtureUi([string]$Action) {
    $c=New-FrameworkFixtureCase;$c.operator.action=$Action;$a=New-FrameworkFixtureAttempt 'ui' 'v2' 123
    $template=($a.events|Where-Object kind -eq 'ui.sample').data.snapshot|ConvertTo-Json -Depth 20
    $a.events=@($a.events|Where-Object {$_.kind -notin @('ui.sample','ui.stable')})
    if($Action -eq 'inflight-rotation'){Add-FrameworkFixtureEvent $a 'session.request' @{request=1}}
    $c.operator.firstSequence=$a.events.Count+1
    $directions=if($Action -in @('rotation','keyboard-rotation','inflight-rotation')){@('Portrait','LandscapeLeft','Portrait','LandscapeRight')}elseif($Action -eq 'window-drag'){@('Portrait','LandscapeLeft','LandscapeRight')}else{@('Portrait','Portrait','Portrait')}
    $frame=10;$index=0
    foreach($direction in $directions){
        $snapshot=$template|ConvertFrom-Json -AsHashtable
        $snapshot.orientation=$direction
        if($direction -ne 'Portrait'){$snapshot.width=1280;$snapshot.height=720;$snapshot.layout.Width=1280;$snapshot.layout.Height=720;$snapshot.layout.mode='Landscape';$snapshot.layout.safeArea.width=1280;$snapshot.layout.safeArea.height=720;$snapshot.safeArea.width=1280;$snapshot.safeArea.height=720;$snapshot.scrolls[0].bounds.right=1280;$snapshot.scrolls[0].bounds.top=720}
        if($Action -eq 'window-drag' -and $index -eq 2){$snapshot.width=1400;$snapshot.layout.Width=1400;$snapshot.safeArea.width=1400}
        if($Action -eq 'keyboard'){$snapshot.keyboardVisible=$index -eq 1}
        if($Action -eq 'keyboard-rotation'){$snapshot.keyboardVisible=$index -gt 0}
        if($Action -eq 'scroll'){$snapshot.scrolls[0].y=1-($index/2.0)}
        if($Action -eq 'background' -and $index -eq 1){Add-FrameworkFixtureEvent $a 'application.pause' @{paused=$true;frame=$frame};$snapshot.selected=0;$snapshot.input.focused=$false}
        if($Action -eq 'background' -and $index -eq 2){Add-FrameworkFixtureEvent $a 'application.pause' @{paused=$false;frame=$frame}}
        Add-FrameworkFixtureEvent $a 'ui.sample' @{frame=$frame;realtime=($frame/10.0);snapshot=$snapshot};$frame++;$index++
    }
    Add-FrameworkFixtureEvent $a 'ui.stable' @{frame=($frame+1);realtime=($frame/10.0);stableFrames=2;snapshot=$snapshot}
    $c.operator.lastSequence=$a.events.Count
    if($Action -eq 'inflight-rotation'){Add-FrameworkFixtureEvent $a 'session.result' @{request=1;Succeeded=$true;error='None';ServerErrorCode=0;epoch=1}}
    $c.attempts=@($a)
    return $c
}
function New-FrameworkFixtureSession([string]$Outcome='Rejected') {
    $e=New-FrameworkFixtureContext;$c=New-FrameworkFixtureCase;$c.id=if($Outcome -eq 'Rejected'){'tok-rejection'}else{'tok-disconnect'};$c.knownGood='v2'
    $c.preservation=New-FrameworkFixturePreservation @('v1','v2')
    $c.server=@{processId=500;binarySha256=('a'*64);kind='TokDemo';evidencePaths=@('synthetic/server.log')}
    $a=New-FrameworkFixtureAttempt $c.id 'v2' 123
    ($a.events|Where-Object {$_.data.code -eq 'CLIENT_VIEW'}).data.message='{"view":5,"presenter":6,"version":"Framework v2","mode":"TokDevelopment"}'
    Add-FrameworkFixtureEvent $a 'session.request' @{request=1}
    Add-FrameworkFixtureEvent $a 'session.state' @{state='Connecting';requests=1;connectionEpoch=1;requestId=1;serverEpoch=$null}
    Add-FrameworkFixtureEvent $a 'session.state' @{state='Authenticating';requests=1;connectionEpoch=1;requestId=1;serverEpoch=$null}
    Add-FrameworkFixtureEvent $a 'session.state' @{state='Disconnected';requests=1;connectionEpoch=1;requestId=0;serverEpoch=$null}
    Add-FrameworkFixtureEvent $a 'session.result' @{request=1;Succeeded=$false;error=$Outcome;ServerErrorCode=if($Outcome -eq 'Rejected'){3}else{0};epoch=$null}
    $c.attempts=@($a);$e.cases=@($c)
    return @{case=$c;evidence=$e}
}
function New-FrameworkFixtureAndroid {
    $e=New-FrameworkFixtureContext;$e.platform='Android';$e.deviceId='emulator-5554';$e.identity.Platform='Android';$e.identity.Architecture='arm64'
    $e.archiveNative=@{'Client.apk'=('a'*64)};$e.nativeBefore=@{'Client.apk'=('a'*64)};$e.nativeAfter=@{'Client.apk'=('a'*64)}
    $e.environment=@{deviceId='emulator-5554';deviceState='device';primaryAbi='x86_64';installedPrimaryAbi='arm64-v8a';translation='libhoudini';kind='LDPlayer';physicalArm='NotRun';installedApkBeforeSha256=('a'*64);installedApkAfterSha256=('a'*64);installCount=1;apkCertificateSha256=('b'*64);archiveNativeLibraries=@{'lib/arm64-v8a/libil2cpp.so'=('c'*64);'lib/arm64-v8a/libunity.so'=('d'*64)};installedNativeLibraries=@{'lib/arm64-v8a/libil2cpp.so'=('c'*64);'lib/arm64-v8a/libunity.so'=('d'*64)}}
    return $e
}
function New-FrameworkFixturePreservation([string[]]$Versions) {
    $context=New-FrameworkFixtureContext;$before=@{};$after=@{}
    foreach($version in $Versions){foreach($path in $context.releases[$version].files.Keys){$before[$version+'/'+$path]=$context.releases[$version].files[$path];$after[$version+'/'+$path]=$context.releases[$version].files[$path]}}
    return @{before=$before;after=$after}
}
function New-FrameworkFixtureTransfer {
    $holding=@{runId='d2unit';caseId='kill-download-partial';transferId=('a'*32);serverProcessId=321;releaseId='v2';manifestSha256=('4'*64);relativePath='Hotfix/Client.Game.dll';sha256=('5'*64);expectedBytes=10;contentLength=10;bytesSent=5;phase='Holding';utc='2026-10-30T00:00:02Z';completed=$false}
    $terminal=$holding.Clone();$terminal.phase='Abort';$terminal.reason='owned-kill';$terminal.nativeProcessId=123;$terminal.utc='2026-10-30T00:01:01Z'
    return @{holding=$holding;abort=$terminal}
}
function New-FrameworkFixtureKillBoundary([string]$Point) {
    $c=New-FrameworkFixtureCase;$c.id='kill-'+$Point.Replace('.','-');$first=$c.attempts[0]
    $purpose=if($Point.StartsWith('state.')){'candidate'}else{$null}
    $selection=@{ReleaseId='v2';Epoch=1;ManifestSha256=('4'*64);Root='C:/native/Releases/player/slots/v2'}
    $state=@{KnownGood='v1';PreviousGood=$null;Candidate='v2';Attempt=if($Point -in @('trial.persisted','hotentry.executing')){1}else{0};BadReleaseIds=@()}
    if($Point -eq 'hotentry.executing'){
        $first=New-FrameworkFixtureAttempt $c.id 'v2' 123
        ($first.events|Where-Object kind -eq 'state.before').data=@{KnownGood='v1';PreviousGood=$null;Candidate=$null;Attempt=0;BadReleaseIds=@()}
        $cut=($first.events|Where-Object {$_.data.code -eq 'CLIENT_ENTRY_EXECUTING'}).sequence
        $first.events=@($first.events|Where-Object {$_.sequence -le $cut})
        Add-FrameworkFixtureEvent $first 'trial.persisted' @{selection=$selection;state=$state}
        $trial=$first.events[-1];$first.events=@($first.events[0..4])+@($trial)+@($first.events[5..($first.events.Count-2)])
        for($i=0;$i -lt $first.events.Count;$i++){$first.events[$i].sequence=$i+1}
    }else{$first.events=@($first.events|Where-Object kind -ne 'download.partial')}
    foreach($event in $first.events){$event.caseId=$c.id};$first.caseId=$c.id;$first.process.caseId=$c.id
    $data=switch($Point){'rename.before' {@{releaseId='v2';manifestSha256=('4'*64)}};{$_ -like 'state.*'} {@{purpose='candidate';releaseId='v2';state=$state}};'trial.persisted' {@{selection=$selection;state=$state}};'hotentry.executing' {@{selection=$selection;message='Client.Game.Entry'}}}
    Add-FrameworkFixtureEvent $first $Point $data
    $first.checkpoint=@{runId='d2unit';caseId=$c.id;attemptId=$first.attemptId;processId=123;point=$Point;purpose=$purpose;token="d2unit:$($c.id):$($first.attemptId):123:$($Point):$purpose";data=$data}
    $c.kill.point=$Point;$c.kill.attemptId=$first.attemptId
    $version=if($Point -eq 'state.replace.after'){'v2'}else{'v1'};$next=New-FrameworkFixtureAttempt $c.id $version 124
    if($Point -eq 'state.replace.after'){
        ($next.events|Where-Object kind -eq 'state.before').data=$state
        $c.preparedCandidate=@{releaseId='v2';manifestSha256=('4'*64);root=$selection.Root;files=(New-FrameworkFixtureContext).releases.v2.files}
    }
    if($Point -in @('trial.persisted','hotentry.executing')){($next.events|Where-Object kind -eq 'state.before').data=$state;($next.events|Where-Object kind -eq 'launch.result').data.state.BadReleaseIds=@('v2')}
    $c.attempts=@($first,$next);return $c
}
function New-FrameworkFixtureTransferFault([string]$Id) {
    $c=New-FrameworkFixtureFault;$c.id=$Id;$c.fault.kind=$Id;$reason=if($Id -eq 'download-cancel'){'cancel'}else{'network-interruption'}
    $c.transfer.holding.caseId=$Id;$c.transfer.abort.caseId=$Id;$c.transfer.abort.reason=$reason
    if($Id -eq 'network-interruption'){
        $first=New-FrameworkFixtureAttempt $Id 'v1' 123
        ($first.events|Where-Object kind -eq 'launch.result').data.failures=@(@{ReleaseId='v2';Epoch=1;phase='Prepare';Code='PreparationFailed';error='Network download interrupted before Content-Length.'})
        $c.attempts=@($first)
    }else{
        $first=$c.attempts[0];($first.events|Where-Object kind -eq 'launch.result').data.selection=$null
        $failure=($first.events|Where-Object kind -eq 'launch.result').data.failures[0];$failure.Code='PreparationCancelled';$failure.error='System.OperationCanceledException: Preparation cancelled.'
        $first.process.acceptance=@{runId='d2unit';caseId=$Id;cancelOnTransfer=$true}
    }
    foreach($attempt in $c.attempts){$attempt.caseId=$Id;$attempt.process.caseId=$Id;foreach($event in $attempt.events){$event.caseId=$Id}}
    Add-FrameworkFixtureEvent $first 'preparation' @{releaseId='v2';manifestSha256=('4'*64);destination='C:/native/Releases/player/slots/v2'}
    Add-FrameworkFixtureEvent $first 'download.partial' @{path='C:/native/staging/hash.partial/downloads/0.partial';uri='http://127.0.0.1/Hotfix/Client.Game.dll';bytes=5;expectedBytes=10;serial=2}
    return $c
}
