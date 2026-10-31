# Synthetic file fixtures; never native acceptance evidence.
function New-FrameworkExecutionFixture([string]$FixtureRoot,[string]$Name){
    $f=New-FrameworkFileBundle (Join-Path $FixtureRoot $Name);$archive=Read-FrameworkArchive $f.archive $f.result Windows64;$proofs=@()
    foreach($version in @('v1','v2')){
        $attempt=[Guid]::NewGuid().ToString('N');$events=@();$selection=@{ReleaseId=$version;Epoch=1;ManifestSha256=('a'*64)}
        $content=@(@('process.start',@{}),@('native.runtime',@{platform='WindowsPlayer';unityVersion='2022.3.25f1';pointerBits=64;applicationId='';profilePackageId='com.sgame.client'}),@('native.identity',@{player=$archive.identity;profilePackageId='com.sgame.client'}),@('metadata',@{message='F6_METADATA player mscorlib.dll OK';selection=$selection}),@('hot.log',@{code='CLIENT_CODE_VERSION';message=$version}),@('hot.log',@{code='CLIENT_VIEW';message=(@{version=('Framework '+$version);mode='LocalSimulation';view=-5;presenter=6}|ConvertTo-Json -Compress)}))
        if($version -ceq 'v2'){$content+=,@('hot.log',@{code='CLIENT_PARSER_EXECUTED';message='AddedMessage list,map,unknown-fields,uint64,hot-generics,empty-registry'})}
        $content+=,@('launch.result',@{ready=$true;restartRequired=$false;metadataCount=1;resourceAttempts=1;selection=$selection;failures=@();hotAssemblies=@('Client.Game.Protocol','Client.Game')})
        foreach($entry in $content){$events+=@{runId='d1fixture';caseId=$version;attemptId=$attempt;processId=$(if($version -ceq 'v1'){123}else{124});sequence=$events.Count+1;utc='2026-10-31T00:00:00Z';kind=$entry[0];data=$entry[1]}}
        $path=Join-Path $f.root ($version+'-events.jsonl');[IO.File]::WriteAllText($path,(($events|ForEach-Object{$_|ConvertTo-Json -Depth 20 -Compress})-join "`n")+"`n")
        $proofs+=@{version=$version;path=$path;sha256=(Get-FrameworkFileHash $path)}
    }
    $gate=@{status='Released';finalD1ArchivesApproved=$true;platform='Windows64';playerBuildId='player';archiveSha256=$archive.archiveSha256;producerCommit=('a'*40);d1Evidence=$proofs};$path=Join-Path $f.root 'execution-gate.json';Write-FrameworkJsonNew $path $gate
    return @{fixture=$f;archive=$archive;gate=$gate;gatePath=$path}
}
