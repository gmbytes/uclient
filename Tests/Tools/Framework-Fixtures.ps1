# Synthetic records matching NativeEvidence/NativeClientHost at the frozen D1 contract.
# These records never establish actual native acceptance.
function New-FrameworkFixtureContext {
    return @{schemaVersion=1;evidenceKind='SyntheticUnit';runId='d2unit';platform='Windows64';playerBuildId='player';
        identity=@{PlayerBuildId='player';Platform='StandaloneWindows64';Architecture='x64';UnityVersion='2022.3.25f1';ContractVersion='1';EnvelopeSha256=('e'*64);AotMetadataSetId=('f'*64)};
        archiveNative=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};nativeBefore=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};nativeAfter=@{'Client.exe'=('a'*64);'GameAssembly.dll'=('b'*64)};
        metadataNames=@('mscorlib.dll','System.Runtime.CompilerServices.Unsafe.dll');metadataHashes=@{'mscorlib.dll'=('7'*64);'System.Runtime.CompilerServices.Unsafe.dll'=('8'*64)};
        releases=@{v1=@{manifestSha256=('1'*64);dllSha256=('2'*64);tableSha256=('3'*64);files=@{file=('c'*64)}};v2=@{manifestSha256=('4'*64);dllSha256=('5'*64);tableSha256=('6'*64);files=@{file=('d'*64)}}};cases=@()}
}
function Add-FrameworkFixtureEvent($Attempt,[string]$Kind,$Data) {
    $Attempt.events+=@{runId='d2unit';caseId=$Attempt.caseId;attemptId=$Attempt.attemptId;processId=$Attempt.processId;sequence=($Attempt.events.Count+1);utc='2026-10-30T00:00:01Z';kind=$Kind;data=$Data}
}
function New-FrameworkFixtureAttempt([string]$CaseId='update-v2',[string]$Version='v2',[int]$ProcessId=123) {
    $context=New-FrameworkFixtureContext
    $selection=@{ReleaseId=$Version;Epoch=1;ManifestSha256=$context.releases[$Version].manifestSha256;Root="C:/native/Releases/player/slots/$Version"}
    $state=@{KnownGood=$Version;PreviousGood='v1';Candidate=$null;Attempt=0;BadReleaseIds=@()}
    $a=@{caseId=$CaseId;attemptId=([Guid]::NewGuid().ToString('N'));processId=$ProcessId;observedProcessId=$ProcessId;events=@();process=@{runId='d2unit';caseId=$CaseId;processId=$ProcessId;playerBuildId='player';startedUtc='2026-10-30T00:00:00Z';finishedUtc='2026-10-30T00:02:00Z';terminal=$true};evidencePaths=@('synthetic/events.jsonl');slotInventory=@{releaseId=$Version;manifestSha256=$selection.ManifestSha256;root=$selection.Root;dllSha256=$context.releases[$Version].dllSha256;tableSha256=$context.releases[$Version].tableSha256;metadata=@{'mscorlib.dll'=('7'*64);'System.Runtime.CompilerServices.Unsafe.dll'=('8'*64)}}}
    Add-FrameworkFixtureEvent $a 'process.start' @{utc='2026-10-30T00:00:01Z'}
    Add-FrameworkFixtureEvent $a 'native.identity' @{player=$context.identity;platform='WindowsPlayer';pointerBits=64;profileSha256=('9'*64);releaseRoot='C:/native/Releases/player';applicationId='com.sgame.client'}
    Add-FrameworkFixtureEvent $a 'state.before' $state
    foreach($phase in @('Select','ResourceInit','Metadata','Hotfix','LoginReady','Ready')){Add-FrameworkFixtureEvent $a 'release.phase' @{phase=$phase;selection=$selection;resourceAttempts=1;metadataCount=2;frame=1}}
    foreach($name in $context.metadataNames){Add-FrameworkFixtureEvent $a 'metadata' @{message="F6_METADATA player $name OK";selection=$selection}}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_ENTRY_EXECUTING';message='Client.Game.Entry'}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_CODE_VERSION';message=$Version}
    $parser=if($Version -eq 'v1'){'BaselineMessage'}else{'AddedMessage'}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_PARSER_EXECUTED';message="$parser list,map,unknown-fields,uint64,hot-generics,empty-registry"}
    Add-FrameworkFixtureEvent $a 'hot.log' @{code='CLIENT_VIEW';message=(@{view=5;presenter=6;version=$Version;mode='LocalSimulation'}|ConvertTo-Json -Compress)}
    Add-FrameworkFixtureEvent $a 'launch.result' @{ready=$true;restartRequired=$false;selection=$selection;state=$state;metadataCount=2;resourceAttempts=1;hotAssemblies=@('Client.Game.Protocol','Client.Game');failures=@();network=@()}
    $snapshot=@{width=720;height=1280;safeArea=@{x=0;y=0;width=720;height=1280};orientation='Portrait';keyboardVisible=$false;keyboardArea=@{x=0;y=0;width=0;height=0};selected=10;view=5;input=@{id=10;textSha256=('c'*64);length=4;focused=$true;caret=4;selectionAnchor=4;selectionFocus=4;bounds=@{left=10;right=710;bottom=700;top=780}};layout=@{Width=720;Height=1280;Revision=1;mode='Portrait';safeArea=@{x=0;y=0;width=720;height=1280}};buttons=@(@{id=11;name='Submit';active=$true;interactable=$true;bounds=@{left=10;right=710;bottom=500;top=580}});scrolls=@(@{id=12;x=0;y=1;bounds=@{left=0;right=720;bottom=0;top=1280}})}
    Add-FrameworkFixtureEvent $a 'ui.sample' @{frame=10;realtime=1;snapshot=$snapshot}
    Add-FrameworkFixtureEvent $a 'ui.stable' @{frame=12;realtime=1.04;stableFrames=2;snapshot=$snapshot}
    return $a
}
function New-FrameworkFixtureCase([string]$Kind='strong-kill') {
    $first=New-FrameworkFixtureAttempt 'kill-download-partial' 'v1' 123
    $recovery=New-FrameworkFixtureAttempt 'kill-download-partial' 'v1' 124
    $first.events=@($first.events|Select-Object -First 3)
    $data=@{path='C:/native/staging/hash.partial/downloads/0.partial';uri='http://127.0.0.1/release/file';bytes=5;expectedBytes=10;serial=2}
    Add-FrameworkFixtureEvent $first 'download.partial' $data
    $first.checkpoint=@{runId='d2unit';caseId=$first.caseId;attemptId=$first.attemptId;processId=123;point='download.partial';purpose=$null;token="d2unit:$($first.caseId):$($first.attemptId):123:download.partial:";data=$data}
    return @{id=$first.caseId;executed=$true;attempts=@($first,$recovery);knownGood='v1';candidate='v2';preservation=@{before=@{'v1/file'=('c'*64)};after=@{'v1/file'=('c'*64)}};kill=@{processId=123;attemptId=$first.attemptId;point='download.partial';utc='2026-10-30T00:01:00Z';action='TerminateProcess';terminal=$true};operator=@{name='unit';utc='2026-10-30T00:00:01Z';environment='synthetic';action='resize';firstSequence=17;lastSequence=18;screenshots=@(@{path='synthetic/screen.png';sha256=('d'*64)})}}
}
function New-FrameworkFixtureFault([switch]$Semantic) {
    $id=if($Semantic){'semantic-version-table'}else{'bad-hash'}
    $c=New-FrameworkFixtureCase;$c.id=$id
    $first=New-FrameworkFixtureAttempt $id 'v2' 123
    $first.events=@($first.events|Where-Object {$_.kind -in @('process.start','native.identity','state.before') -or ($_.kind -eq 'release.phase' -and $_.data.phase -eq 'ResourceInit' -and $Semantic)})
    $state=@{KnownGood='v1';PreviousGood=$null;Candidate=$null;Attempt=0;BadReleaseIds=@('v2')}
    $first.events[2].data=$state
    if($Semantic){Add-FrameworkFixtureEvent $first 'native.error' @{message='GameFramework version table deserialize failed';selection=@{ReleaseId='v2';ManifestSha256=('4'*64);Root='C:/native/Releases/player/slots/v2'}}}
    Add-FrameworkFixtureEvent $first 'launch.result' @{ready=$false;restartRequired=[bool]$Semantic;selection=@{ReleaseId='v2';ManifestSha256=('4'*64);Root='C:/native/Releases/player/slots/v2'};state=$state;metadataCount=0;resourceAttempts=([int][bool]$Semantic);hotAssemblies=@();failures=@(@{ReleaseId='v2';Epoch=1;phase=if($Semantic){'Resources'}else{'Prepare'};Code=if($Semantic){'ResourceInitializationFailed'}else{'PreparationFailed'};error=if($Semantic){'GameFramework version table deserialize failed'}else{'SHA256 hash mismatch'}})}
    for($i=0;$i -lt $first.events.Count;$i++){$first.events[$i].sequence=$i+1}
    $c.attempts=@($first,(New-FrameworkFixtureAttempt $id 'v1' 124))
    $c.fault=@{kind=$id;candidate='v2';manifestSha256=('4'*64);payloadSha256=('a'*64);tableSha256=('6'*64);inputPaths=@('synthetic/fault.json');authenticated=[bool]$Semantic}
    return $c
}
