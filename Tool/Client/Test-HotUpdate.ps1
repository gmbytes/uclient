[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Android','Windows64')][string]$Platform,[string]$DeviceId,[string]$UnityEditor,[switch]$BuildOnly,[string]$RunRoot)
. "$PSScriptRoot/Unity-Common.ps1"
. "$PSScriptRoot/Assert-HotUpdateEvidence.ps1"
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$env:SGAME_COMM_ROOT='E:/sgame/comm'
$env:ANDROID_SDK_ROOT='E:/sgame/.tools/unity-android-2022.3.25f1/sdk'
$env:ANDROID_NDK_ROOT='E:/sgame/.tools/unity-android-2022.3.25f1/ndk/android-ndk-r23b'
$env:JAVA_HOME='E:/sgame/.tools/unity-android-2022.3.25f1/jdk11'
$env:SDK_TEST_BASE_URL='file:///E:/sgame/client/Builds/tools/sdk-metadata/'
$adb=Join-Path $env:ANDROID_SDK_ROOT 'platform-tools/adb.exe'
$server=$null;$reverse=$false;$process=$null;$commands=[Collections.Generic.List[object]]::new()
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Inventory([string]$Root){$r=[ordered]@{};foreach($f in Get-ChildItem -LiteralPath $Root -File -Recurse|Sort-Object FullName){$r[$f.FullName.Substring($Root.Length+1).Replace('\','/')]=Hash $f.FullName};return $r}
function NativeCommand([string]$File,[string[]]$Arguments,[string]$Log){$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$File;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;foreach($a in $Arguments){$start.ArgumentList.Add($a)};$p=[Diagnostics.Process]::Start($start);$out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync();$p.WaitForExit();$text=$out.Result+$err.Result;if($Log){[IO.File]::WriteAllText($Log,$text)};$commands.Add(@{file=$File;arguments=$Arguments;exitCode=$p.ExitCode;log=$Log;utc=[DateTime]::UtcNow});if($p.ExitCode-ne0){throw "$File exited $($p.ExitCode): $text"};return $text}
function Adb([string[]]$Arguments,[string]$Log){NativeCommand $adb (@('-s',$DeviceId)+$Arguments) $Log}
function StopServer {
    if($null-ne$server){[IO.File]::WriteAllText((Join-Path $RunRoot 'server-stop'),'stop');if(-not$server.WaitForExit(5000)){$server.Kill();$server.WaitForExit()};$commands.Add(@{action='owned-server-stop';pid=$server.Id;exitCode=$server.ExitCode});$script:server=$null}
}
function StartServer {
    Remove-Item -LiteralPath (Join-Path $RunRoot 'server-stop'),(Join-Path $RunRoot 'server-ready') -ErrorAction SilentlyContinue
    $script:server=Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',"`"$PSScriptRoot/Serve-HotUpdate.ps1`"",'-RunRoot',"`"$RunRoot`"",'-Port',$port) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RunRoot 'server.stdout.log') -RedirectStandardError (Join-Path $RunRoot 'server.stderr.log')
    $until=[DateTime]::UtcNow.AddSeconds(10);while(-not(Test-Path -LiteralPath (Join-Path $RunRoot 'server-ready'))){if($server.HasExited -or [DateTime]::UtcNow-gt$until){throw 'Owned loopback server did not start.'};Start-Sleep -Milliseconds 100}
    $commands.Add(@{action='owned-server-start';pid=$server.Id;port=$port})
}
if(-not$RunRoot){
    $fixtureRoot=Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/Generated'
    $provenance=Get-Content -LiteralPath (Join-Path $fixtureRoot 'Pristine/protocol-manifest.json') -Raw|ConvertFrom-Json
    foreach($p in $provenance.generated_sha256.PSObject.Properties){if((Hash (Join-Path $fixtureRoot ('Pristine/'+$p.Name)))-cne$p.Value){throw 'Protocol generated fixture provenance differs.'}}
    foreach($p in $provenance.source_sha256.PSObject.Properties){$source=if($p.Name-eq'tok/client/v1/client.proto'){Join-Path $env:SGAME_COMM_ROOT ('proto/'+$p.Name)}else{Join-Path $env:SGAME_COMM_ROOT ('proto/tests/fixtures/Protocols/F6AotProbe/'+$p.Name)};if((Hash $source)-cne$p.Value){throw 'Canonical protocol fixture source differs.'}}
    $runId=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $RunRoot=Join-Path $projectRoot "Builds/HotUpdate/$Platform/$runId";New-Item -ItemType Directory -Path $RunRoot|Out-Null
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
    Write-RunJson (Join-Path $RunRoot 'run.json') @{runId=$runId;platform=$Platform;port=$port;sourceCommit=(& git -C $projectRoot rev-parse HEAD);createdUtc=[DateTime]::UtcNow}
    $editor=Resolve-UnityEditor $UnityEditor
    & "$PSScriptRoot/Build-LinkerPolicy.ps1" -UnityEditor $editor
    if($LASTEXITCODE-ne0){throw 'Linker helper failed.'}
    $key=[Security.Cryptography.RSA]::Create(2048);$keyPath=Join-Path $RunRoot 'ephemeral-test-key.xml';[IO.File]::WriteAllText($keyPath,$key.ToXmlString($true));$public=$key.ExportParameters($false);$key.Dispose()
    $profile=(Get-Content -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixVersions/Profile.cs.txt') -Raw).Replace('__PORT__',[string]$port).Replace('__MODULUS__',[Convert]::ToBase64String($public.Modulus)).Replace('__EXPONENT__',[Convert]::ToBase64String($public.Exponent))
    $temporary=@{
        'Assets/Scripts/Framework/Bootstrap/F8Runtime.cs'='Tests/Fixtures/HotfixVersions/Runtime.cs.txt'
        'Assets/Scripts/Game/F8Entry.cs'='Tests/Fixtures/HotfixVersions/Entry.cs.txt'
        'Assets/Scripts/Game/F8VersionBranch.cs'='Tests/Fixtures/HotfixVersions/V1/VersionBranch.cs.txt'
        'Assets/Scripts/Game/Protocol/F8ProtocolProbe.cs'='Tests/Fixtures/HotfixAotProbe/ProtocolProbe.cs.txt'
        'Assets/Scripts/Game/Protocol/F8GeneratedMessage.cs'='Tests/Fixtures/HotfixAotProbe/Generated/Pristine/AotProbe.cs'
        'Assets/Scripts/Game/Protocol/F8GeneratedRegistry.cs'='Tests/Fixtures/HotfixAotProbe/Generated/AdaptedRegistry.cs.txt'
    }
    $pinPath='Assets/Scripts/Framework/HotUpdate/F8ProofProfile.cs'
    foreach($p in @($temporary.Keys)+$pinPath){if(Test-Path -LiteralPath (Join-Path $projectRoot $p)){throw "Owned generated profile already exists: $p"}}
    $restore=@('Assets/Scripts/Game/Client.Game.asmdef','Assets/Scripts/Game/Protocol/Client.Game.Protocol.asmdef','Assets/Scripts/Framework/Bootstrap/Client.Bootstrap.asmdef','Assets/Scenes/Bootstrap.unity','Assets/Prefabs/App/UIUpdate.prefab','Assets/Prefabs/App/GameRoot.prefab','Assets/Prefabs/App/GameFramework.prefab','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/HybridCLRSettings.asset','ProjectSettings/ProjectSettings.asset','Assets/Scripts/Framework/HotUpdate/Generated/link.xml','Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs','Assets/Settings/Environment/LocalSimulationProfile.asset')
    # UGF's first decorated provider may return null, selecting its default path.
    # Preserve that effective path as well as the intended run-local path; never patch the vendor resolver here.
    $effectiveConfig='Assets/GameFramework/Configs/ResourceCollection.xml'
    $restore+=@($effectiveConfig,$effectiveConfig+'.meta','Assets/GameFramework/Configs.meta','Assets/GameFramework.meta')
    $original=@{};foreach($p in $restore){$full=Join-Path $projectRoot $p;$original[$p]=if(Test-Path -LiteralPath $full -PathType Leaf){[IO.File]::ReadAllBytes($full)}else{$null}}
    $generated=@('Assets/StreamingAssets/F8Baseline','Assets/StreamingAssets/F8PlayerIdentity.json','Assets/Tests/Generated/F8.unity')
    foreach($p in $generated){if(Test-Path -LiteralPath (Join-Path $projectRoot $p)){throw "Generated output already exists: $p"}}
    try {
        foreach($p in $temporary.Keys){Copy-Item -LiteralPath (Join-Path $projectRoot $temporary[$p]) -Destination (Join-Path $projectRoot $p)}
        [IO.File]::WriteAllText((Join-Path $projectRoot $pinPath),$profile)
        foreach($p in $restore|Where-Object{$_-like'*.asmdef'}){$j=Get-Content -LiteralPath (Join-Path $projectRoot $p) -Raw|ConvertFrom-Json;$ref=if($p-like'*Bootstrap*'){'UnityGameFramework.Runtime'}else{'Client.Protocol.Envelope'};if($j.references-notcontains$ref){$j.references+= $ref};Write-RunJson (Join-Path $projectRoot $p) $j}
        $publicRoot=Join-Path $RunRoot 'PublicInputs';New-Item -ItemType Directory -Path $publicRoot|Out-Null
        foreach($p in @($temporary.Keys)+$pinPath+$restore){if(-not(Test-Path -LiteralPath (Join-Path $projectRoot $p) -PathType Leaf)){continue};$destination=Join-Path $publicRoot $p;New-Item -ItemType Directory -Force (Split-Path -Parent $destination)|Out-Null;Copy-Item -LiteralPath (Join-Path $projectRoot $p) -Destination $destination}
        Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixVersions') -Destination (Join-Path $publicRoot 'Templates') -Recurse
        Write-RunJson (Join-Path $RunRoot 'public-input-hashes.json') (Inventory $publicRoot)
        foreach($version in @('Native','v2','throw')){
            if($version-ne'Native'){$name=if($version-eq'v2'){'V2'}else{'ThrowOnStart'};Copy-Item -LiteralPath (Join-Path $projectRoot "Tests/Fixtures/HotfixVersions/$name/VersionBranch.cs.txt") -Destination (Join-Path $projectRoot 'Assets/Scripts/Game/F8VersionBranch.cs')}
            $args=@('-batchmode','-nographics','-quit','-projectPath',$projectRoot,'-buildTarget',$(if($Platform-eq'Android'){'Android'}else{'Win64'}),'-executeMethod','Client.Editor.Build.HotUpdateProofBuild.Run','-f8Output',(Join-Path $RunRoot 'Build'),'-f8Platform',$Platform,'-f8Mode',$(if($version-eq'Native'){'Native'}else{'Patch'}),'-f8Version',$version,'-f8Key',$keyPath,'-f8LinkerPolicy',(Join-Path $projectRoot 'Builds/LinkerPolicy/policy.json'),'-hybridclrSource','E:/sgame/client/Builds/dependencies/assembled-libil2cpp','-logFile',(Join-Path $RunRoot "Editor-$version.log"))
            Write-Host "F8_BUILD_PHASE $Platform $version $RunRoot"
            $sourceHashes=[ordered]@{};foreach($path in @(& git -C $projectRoot ls-files)+@(& git -C $projectRoot ls-files --others --exclude-standard)){if(Test-Path -LiteralPath (Join-Path $projectRoot $path) -PathType Leaf){$sourceHashes[$path]=Hash (Join-Path $projectRoot $path)}}
            foreach($path in @($temporary.Keys)+$pinPath){$sourceHashes[$path]=Hash (Join-Path $projectRoot $path)}
            Write-RunJson (Join-Path $RunRoot "source-$version.json") $sourceHashes
            $code=Invoke-Unity $editor $args;$commands.Add(@{file=$editor;arguments=$args;exitCode=$code;phase=$version});Write-RunJson (Join-Path $RunRoot 'build-commands.json') $commands
            foreach($configPath in @((Join-Path $RunRoot 'Build/ResourceCollection.xml'),(Join-Path $projectRoot $effectiveConfig))){if(Test-Path -LiteralPath $configPath){$capture=Join-Path $RunRoot ("ResourceCollection-"+$version+"-"+[IO.Path]::GetFileName([IO.Path]::GetDirectoryName($configPath))+'.xml');Copy-Item -LiteralPath $configPath -Destination $capture;Write-RunJson ($capture+'.provenance.json') @{source=$configPath;sha256=(Hash $capture);phase=$version}}}
            if($code-ne0){throw "Unity $version exited $code"}
        }
    }finally{
        foreach($p in @($temporary.Keys)+$pinPath){Remove-Item -LiteralPath (Join-Path $projectRoot $p),(Join-Path $projectRoot ($p+'.meta')) -ErrorAction SilentlyContinue}
        foreach($p in $restore){$full=Join-Path $projectRoot $p;if($null-ne$original[$p]){[IO.File]::WriteAllBytes($full,$original[$p])}else{Remove-Item -LiteralPath $full -ErrorAction SilentlyContinue}}
        foreach($p in $generated){$full=[IO.Path]::GetFullPath((Join-Path $projectRoot $p));if(-not$full.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe owned cleanup.'};if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force};Remove-Item -LiteralPath ($full+'.meta') -ErrorAction SilentlyContinue}
    }
}else{$RunRoot=[IO.Path]::GetFullPath($RunRoot);$config=Get-Content -LiteralPath (Join-Path $RunRoot 'run.json') -Raw|ConvertFrom-Json;if($config.platform-ne$Platform){throw 'Run platform mismatch.'};$runId=$config.runId;$port=$config.port}
Write-Host "F8_EVIDENCE $RunRoot"
if($BuildOnly){Write-Host 'F8_BUILD_ONLY_COMPLETE';exit 0}
if(Test-Path -LiteralPath (Join-Path $RunRoot 'hotupdate-result.json')){throw 'This run already has native evidence; preserve it and use a fresh buildRun.'}
$archive=Get-Content -LiteralPath (Join-Path $RunRoot 'Build/Archive/archive.json') -Raw|ConvertFrom-Json
$playerRoot=Join-Path $RunRoot 'Build/Player';$nativeBefore=Inventory $playerRoot
$result=[ordered]@{schemaVersion=1;task='F8';runId=$runId;platform=$Platform;playerBuildId=$archive.playerBuildId;nativeBefore=$nativeBefore;nativeAfter=$null;metadataNames=@($archive.metadata|ForEach-Object path);aotNames=@($archive.metadata|ForEach-Object{[IO.Path]::GetFileNameWithoutExtension($_.path)});cases=@();passed=$false;physicalArm='NotRun';iOS='NotRun';scope='Fresh native Player; endpoint unavailable means owned update endpoint, not radio disable';archive=(Join-Path $RunRoot 'Build/Archive/archive.json')}
foreach($v in @('v1','v2')){$result[$v+'DllHash']=Hash (Join-Path $RunRoot "Build/Releases/$v/Hotfix/Client.Game.dll");$result[$v+'ResourceHash']=Hash (Join-Path $RunRoot "Build/Releases/$v/Resources/GameFrameworkVersion.dat");$result[$v+'ManifestHash']=Hash (Join-Path $RunRoot "Build/Releases/$v/release.json")}
$output=Join-Path $RunRoot 'Runtime';New-Item -ItemType Directory -Force $output|Out-Null
try {
    if($Platform-eq'Android'){
        if(-not$DeviceId){throw 'Android requires an explicit selected device.'};$result.deviceId=$DeviceId
        $state=(Adb @('get-state') (Join-Path $RunRoot 'device-state.txt')).Trim();if($state-ne'device'){throw 'Selected Android device is unavailable.'};$result.deviceState=$state
        $result.deviceProperties=Adb @('shell','getprop') (Join-Path $RunRoot 'device-properties.txt')
        $existing=Adb @('shell','pm','path','com.sgame.client.hotupdateproof') $null;if($existing.Trim()){throw 'Proof package already installed; refuse to overwrite an existing scenario.'}
        $apk=Join-Path $playerRoot 'Client.apk';$buildTools=Join-Path $env:ANDROID_SDK_ROOT 'build-tools/32.0.0'
        $result.apkBadging=NativeCommand (Join-Path $buildTools 'aapt.exe') @('dump','badging',$apk) (Join-Path $RunRoot 'apk-badging.txt')
        if($result.apkBadging-notmatch"native-code: 'arm64-v8a'"){throw 'APK is not ARM64-only.'}
        $result.apkCertificate=NativeCommand (Join-Path $env:JAVA_HOME 'bin/java.exe') @('-jar',(Join-Path $buildTools 'lib/apksigner.jar'),'verify','--print-certs',$apk) (Join-Path $RunRoot 'apk-certificate.txt')
        Adb @('install','--abi','arm64-v8a',$apk) (Join-Path $RunRoot 'install-once.txt')|Out-Null;$result.installCount=1
        $installed=(Adb @('shell','pm','path','com.sgame.client.hotupdateproof') (Join-Path $RunRoot 'installed-package-path.txt')).Trim().Substring(8)
        Adb @('pull',$installed,(Join-Path $RunRoot 'installed-base.apk')) (Join-Path $RunRoot 'installed-package-pull.txt')|Out-Null
        if((Hash (Join-Path $RunRoot 'installed-base.apk'))-cne(Hash $apk)){throw 'Installed APK differs from native archive.'}
        $result.installedPackage=Adb @('shell','dumpsys','package','com.sgame.client.hotupdateproof') (Join-Path $RunRoot 'installed-package.txt')
        Adb @('reverse',"tcp:$port","tcp:$port") (Join-Path $RunRoot 'reverse-create.txt')|Out-Null;$reverse=$true
    }
    foreach($case in @('v1','v2','throw','fallback','offline')){
        if($case-eq'v2'){[IO.File]::WriteAllText((Join-Path $RunRoot 'active-release.txt'),'v2');StartServer}
        if($case-eq'throw'){[IO.File]::WriteAllText((Join-Path $RunRoot 'active-release.txt'),'throw')}
        if($case-eq'offline'){StopServer;if($reverse){Adb @('reverse','--remove',"tcp:$port") (Join-Path $RunRoot 'reverse-remove.txt')|Out-Null;$reverse=$false}}
        Write-Host "F8_RUN_PHASE $Platform $case"
        if($Platform-eq'Windows64'){
            $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $playerRoot 'Client.exe';$start.UseShellExecute=$false;$start.CreateNoWindow=$true
            $args=@('-f8Run',$runId,'-f8Case',$case,'-f8Store',(Join-Path $RunRoot 'Installed'),'-f8Output',$output,'-screen-width','720','-screen-height','960','-logFile',(Join-Path $output "$case.log"));foreach($a in $args){$start.ArgumentList.Add($a)}
            $process=[Diagnostics.Process]::Start($start);$pidOwned=$process.Id;$natural=$process.WaitForExit(180000);if(-not$natural){$process.Kill();$process.WaitForExit();throw 'Owned Player startup timed out.'};$commands.Add(@{file=$start.FileName;arguments=$args;pid=$pidOwned;exitCode=$process.ExitCode});if($process.ExitCode-ne0){throw "Player exited $($process.ExitCode)"};$process=$null
        }else{
            Adb @('shell','am','force-stop','com.sgame.client.hotupdateproof') $null|Out-Null
            Adb @('shell','am','start','-W','-n','com.sgame.client.hotupdateproof/com.unity3d.player.UnityPlayerActivity','--es','f8Run',$runId,'--es','f8Case',$case) (Join-Path $output "$case-start.txt")|Out-Null
            $remote="/sdcard/Android/data/com.sgame.client.hotupdateproof/files/F8/$runId/$case.json";$until=[DateTime]::UtcNow.AddSeconds(180);$found=$false
            while([DateTime]::UtcNow-lt$until){$probe=Adb @('shell','sh','-c',"'test -f $remote && echo ready || echo pending'") $null;if($probe-match'ready'){$found=$true;break};Start-Sleep -Milliseconds 1000}
            if(-not$found){throw 'Android native result timed out.'}
            Adb @('pull',$remote,(Join-Path $output "$case.json")) (Join-Path $output "$case-pull.txt")|Out-Null
            Adb @('pull',($remote-replace'\.json$','.png'),(Join-Path $output "$case.png")) $null|Out-Null
            Adb @('shell','dumpsys','activity','processes') (Join-Path $output "$case-processes.txt")|Out-Null
            Adb @('logcat','-d','-s','Unity') (Join-Path $output "$case-logcat.txt")|Out-Null
            if($case-eq'v2'){Adb @('shell','input','keyevent','KEYCODE_HOME') (Join-Path $output 'background.txt')|Out-Null;Start-Sleep -Milliseconds 1000;Adb @('shell','am','start','-W','-n','com.sgame.client.hotupdateproof/com.unity3d.player.UnityPlayerActivity') (Join-Path $output 'resume.txt')|Out-Null}
        }
        $record=Get-Content -LiteralPath (Join-Path $output "$case.json") -Raw|ConvertFrom-Json -AsHashtable;$result.cases+=,$record
        if($case-ne'throw' -and -not$record.ready){throw "Required $case launch failed: $($record.launchError) $($record.error)"}
        if((Inventory $playerRoot|ConvertTo-Json -Compress)-cne($nativeBefore|ConvertTo-Json -Compress)){throw 'Native Player hash changed during scenario.'}
        Write-RunJson (Join-Path $RunRoot 'hotupdate-result.json') $result
    }
    $result.nativeAfter=Inventory $playerRoot;Assert-HotUpdateEvidence $result;$result.passed=$true
}catch{$result.error=$_.Exception.ToString();Write-Host $result.error}
finally{
    if($null-ne$process -and -not$process.HasExited){$process.Kill();$process.WaitForExit()}
    StopServer
    if($reverse){Adb @('reverse','--remove',"tcp:$port") (Join-Path $RunRoot 'reverse-cleanup.txt')|Out-Null;$reverse=$false}
    if($Platform-eq'Android' -and $result.Contains('installCount')){Adb @('shell','am','force-stop','com.sgame.client.hotupdateproof') (Join-Path $RunRoot 'final-force-stop.txt')|Out-Null}
    $result.commands=$commands;$result.cleanup=@{ownedServerStopped=($null-eq$server);ownedReverseRemoved=(-not$reverse);ownedPlayerTerminal=$true};Write-RunJson (Join-Path $RunRoot 'hotupdate-result.json') $result
}
if(-not$result.passed){Write-Host 'HOTUPDATE_FAIL';exit 1};Write-Host 'HOTUPDATE_PASS'
