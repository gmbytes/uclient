# Files are deliberately synthetic. No fixture created here is a runnable native package.
function New-FrameworkSyntheticPE {
    $bytes=[byte[]]::new(128);$bytes[0]=77;$bytes[1]=90;$bytes[60]=64;$bytes[64]=80;$bytes[65]=69;$bytes[68]=100;$bytes[69]=134;$bytes[88]=11;$bytes[89]=2;return ,$bytes
}
function New-FrameworkFileBundle([string]$Root) {
    New-Item -ItemType Directory -Path $Root|Out-Null
    $archiveRoot=Join-Path $Root 'Archive';$bundleRoot=Join-Path $Root 'Evidence';New-Item -ItemType Directory -Path $bundleRoot|Out-Null
    $archive=@{schemaVersion=1;playerBuildId='player';platform='StandaloneWindows64';architecture='x64';unityVersion='2022.3.25f1';contractVersion='1';envelopeSha256=('e'*64);aotMetadataSetId=('f'*64);buildProfile='{"development":false}'}
    foreach($pair in @(@('metadata','Metadata','mscorlib.dll'),@('baseline','Baseline','base.bytes'),@('player','Player','Client.exe'),@('compiled','Compiled','Client.Game.dll'),@('inputs','Inputs','Assets/Tests/Generated/D1/NativeProfile.json'),@('facades','Facades','netstandard.dll'))){
        $directory=Join-Path $archiveRoot $pair[1];New-Item -ItemType Directory -Path $directory -Force|Out-Null
        $file=Join-Path $directory $pair[2];[IO.Directory]::CreateDirectory((Split-Path -Parent $file))|Out-Null;[IO.File]::WriteAllText($file,$(if($pair[0] -ceq 'inputs'){'{"packageId":"com.sgame.client"}'}else{'synthetic-'+$pair[0]}))
        $archive[$pair[0]]=@(@{path=$pair[2];length=(Get-Item $file).Length;sha256=(Get-FrameworkFileHash $file)})
    }
    $archive.player=@()
    foreach($name in @('Client.exe','GameAssembly.dll','UnityPlayer.dll')){$file=Join-Path $archiveRoot ('Player/'+$name);[IO.File]::WriteAllBytes($file,(New-FrameworkSyntheticPE));$archive.player+=@{path=$name;length=(Get-Item $file).Length;sha256=(Get-FrameworkFileHash $file)}}
    Write-FrameworkJsonNew (Join-Path $archiveRoot 'archive.json') $archive
    $resultPath=Join-Path $Root 'build-result.json'
    $result=@{Status=1;OutputDirectory=$Root;ResultFile=$resultPath;PlayerBuildId='player';ReleaseId='v1';Errors=@();EvidenceFiles=@((Join-Path $archiveRoot 'archive.json'))}
    Write-FrameworkJsonNew $resultPath $result
    $receipt=@{Status=1;OutputDirectory=$Root;ResultFile=$resultPath}
    $bundle=@{schemaVersion=1;evidenceKind='SyntheticUnit';runId='d2unit';platform='Windows64';archiveSha256=(Get-FrameworkFileHash (Join-Path $archiveRoot 'archive.json'));buildResultSha256=(Get-FrameworkFileHash $resultPath);releases=@{};cases=@()}
    function PutReference([string]$Name,$Value){$path=Join-Path $bundleRoot $Name;[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;Write-FrameworkJsonNew $path $Value;return @{path=$Name;sha256=(Get-FrameworkFileHash $path)}}
    $bundle.buildReceipt=PutReference 'receipt.json' $receipt
    $native=@{};foreach($record in $archive.player){$native[$record.path]=$record.sha256}
    $bundle.nativeBefore=PutReference 'native-before.json' $native
    $bundle.nativeAfter=PutReference 'native-after.json' $native
    foreach($version in @('v1','v2')){
        $dll=PutReference ($version+'/dll.bytes') @{syntheticDll=$version};$protocol=PutReference ($version+'/protocol.bytes') @{syntheticProtocol=$version};$table=PutReference ($version+'/table.bytes') @{syntheticTable=$version}
        $manifest=PutReference ($version+'/release.json') @{releaseId=$version;files=@(@{path='Hotfix/Client.Game.dll';sha256=$dll.sha256;length=(Get-Item (Join-Path $bundleRoot $dll.path)).Length},@{path='Hotfix/Client.Game.Protocol.dll';sha256=$protocol.sha256;length=(Get-Item (Join-Path $bundleRoot $protocol.path)).Length},@{path='Resources/GameFrameworkVersion.dat';sha256=$table.sha256;length=(Get-Item (Join-Path $bundleRoot $table.path)).Length})}
        $bundle.releases[$version]=@{manifest=$manifest;dll=$dll;protocolDll=$protocol;table=$table}
    }
    $bundlePath=Join-Path $bundleRoot 'bundle.json';Write-FrameworkJsonNew $bundlePath $bundle
    return @{root=$Root;archive=$archiveRoot;result=$resultPath;bundlePath=$bundlePath;bundle=$bundle;bundleRoot=$bundleRoot}
}
