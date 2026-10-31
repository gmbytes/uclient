#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$PSScriptRoot/Framework-FileFixtures.ps1"
if(Test-Path "$project/Tool/Client/Framework-FaultRelease.ps1"){. "$project/Tool/Client/Framework-FaultRelease.ps1"}
$root=Join-Path $project ('Builds/Tests/D2/FaultRelease-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$fixture=New-FrameworkFileBundle (Join-Path $root 'fixture');$source=Join-Path $root 'source';$private=Join-Path $root 'private';[IO.Directory]::CreateDirectory($source)|Out-Null;[IO.Directory]::CreateDirectory($private)|Out-Null
$key=[Security.Cryptography.RSA]::Create(2048);$pub=$key.ExportParameters($false)
$profile=@{schemaVersion=1;keyId='test-key';modulus=[Convert]::ToBase64String($pub.Modulus);exponent=[Convert]::ToBase64String($pub.Exponent);packageId='com.sgame.client';endpoint='http://127.0.0.1:18765/';acceptance=$true;sessionHost='127.0.0.1';sessionPort=7000}
$profilePath=Join-Path $root 'public-profile.json';Write-FrameworkJsonNew $profilePath $profile
$archivedProfile=Join-Path $fixture.archive 'Inputs/Assets/Tests/Generated/D1/NativeProfile.json';[IO.File]::Copy($profilePath,$archivedProfile,$true)
$archive=Read-FrameworkJson (Join-Path $fixture.archive 'archive.json');$archive.inputs[0].length=(Get-Item $archivedProfile).Length;$archive.inputs[0].sha256=Get-FrameworkFileHash $archivedProfile
[IO.File]::WriteAllText((Join-Path $fixture.archive 'archive.json'),($archive|ConvertTo-Json -Depth 30))
$keyPath=Join-Path $private 'signer.xml';[IO.File]::WriteAllText($keyPath,$key.ToXmlString($true))
$files=@();foreach($name in @('Hotfix/Client.Game.dll','Hotfix/Client.Game.Protocol.dll','Resources/GameFrameworkVersion.dat','Resources/client/profiles.dat')){$path=Join-Path $source $name;[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,'synthetic-v2-'+$name);$files+=@{path=$name;length=(Get-Item $path).Length;sha256=(Get-FrameworkFileHash $path);kind=$(if($name.StartsWith('Hotfix/')){'HotfixDll'}else{'NativeResource'})}}
foreach($mapping in @(@('Metadata/mscorlib.dll','Metadata/mscorlib.dll','AotMetadata'),@('References/netstandard.dll','Facades/netstandard.dll','Configuration'))){$target=Join-Path $source $mapping[0];[IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null;[IO.File]::Copy((Join-Path $fixture.archive $mapping[1]),$target,$false);$files+=@{path=$mapping[0];length=(Get-Item $target).Length;sha256=(Get-FrameworkFileHash $target);kind=$mapping[2]}}
$manifest=@{schemaVersion=1;keyId='test-key';releaseId='v2';player=@{playerBuildId='player';platform='StandaloneWindows64';architecture='x64';unityVersion='2022.3.25f1';contractVersion='1';envelopeSha256=('e'*64);aotMetadataSetId=('f'*64)};protocolSha256=('c'*64);resourceVersionListPath='Resources/GameFrameworkVersion.dat';files=$files;assemblies=@();assemblyOrder=@()}
function SignSource([string]$Name,$Document){$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Document|ConvertTo-Json -Depth 30 -Compress));[IO.File]::WriteAllBytes((Join-Path $source ($Name+'.json')),$bytes);[IO.File]::WriteAllBytes((Join-Path $source ($Name+'.sig')),$key.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))}
SignSource release $manifest;SignSource channel @{schemaVersion=1;keyId='test-key';releaseId='v2';manifestRelativePath='release.json';manifestSha256=(Get-FrameworkFileHash (Join-Path $source 'release.json'))}
$known=Join-Path $root 'known.json';Write-FrameworkJsonNew $known @{files=@()}
$before=Get-FrameworkInventory $source;$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_FAULT_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_FAULT_FAIL $Name $($_.Exception.Message)"}}
function Publish([string]$Fault,[string]$Output,[string]$Signer=$keyPath){New-FrameworkTestRelease -Platform Windows64 -PlayerArchive $fixture.archive -BuildResult $fixture.result -SourceReleaseDirectory $source -PublicProfile $profilePath -SigningKey $Signer -KnownGoodManifest $known -RunId testfault -CaseId $Fault -ReleaseId ('test-'+$Fault) -Fault $Fault -OutputDirectory $Output}
foreach($faultCase in @('None','bad-signature','bad-hash','wrong-player-build','wrong-architecture','missing-dll','missing-bundle','tampered-version-table','semantic-version-table')){
    Check "publish independent signed fault $faultCase" {
        $output=Join-Path $root $faultCase;$result=Publish $faultCase $output
        $raw=[IO.File]::ReadAllBytes((Join-Path $output 'release.json'));$signature=[IO.File]::ReadAllBytes((Join-Path $output 'release.sig'));$valid=$key.VerifyData($raw,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
        Require ($valid -eq ($faultCase -cne 'bad-signature')) 'Unexpected release signature behavior.'
        $channelBytes=[IO.File]::ReadAllBytes((Join-Path $output 'channel.json'));Require ($key.VerifyData($channelBytes,[IO.File]::ReadAllBytes((Join-Path $output 'channel.sig')),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'Channel signature is invalid.'
        $channel=Read-FrameworkJson (Join-Path $output 'channel.json');$release=Read-FrameworkJson (Join-Path $output 'release.json');Require ($channel.manifestSha256 -ceq (Get-FrameworkFileHash (Join-Path $output 'release.json')) -and $channel.releaseId -ceq ('test-'+$faultCase) -and $release.releaseId -ceq $channel.releaseId) 'Candidate channel does not pin its independent manifest.'
        $bad=@($release.files|Where-Object {-not(Test-Path (Join-Path $output $_.path)) -or (Get-FrameworkFileHash (Join-Path $output $_.path)) -cne $_.sha256})
        Require ($bad.Count -eq $(if($faultCase -cin @('bad-hash','missing-dll','missing-bundle','tampered-version-table')){1}else{0})) 'Unexpected payload corruption count.'
        Require (($release.player.playerBuildId -cne 'player') -eq ($faultCase -ceq 'wrong-player-build')) 'Wrong Player identity fault.'
        Require (($release.player.architecture -cne 'x64') -eq ($faultCase -ceq 'wrong-architecture')) 'Wrong architecture fault.'
        if($faultCase -ceq 'semantic-version-table'){Require ((Get-FrameworkFileHash (Join-Path $output $release.resourceVersionListPath)) -cne (Get-FrameworkFileHash (Join-Path $source $release.resourceVersionListPath))) 'GF table bytes did not change.'}
        Require (-not $result.nativeAcceptance -and -not(Test-Path (Join-Path $output 'signer.xml'))) 'Publisher claimed acceptance or copied private material.'
    }
}
Check 'wrong signer rejects before creating output' {$wrong=[Security.Cryptography.RSA]::Create(2048);$wrongPath=Join-Path $private 'wrong.xml';try{[IO.File]::WriteAllText($wrongPath,$wrong.ToXmlString($true));$caught=$false;try{Publish None (Join-Path $root 'wrong-signer') $wrongPath}catch [IO.InvalidDataException]{$caught=$true};Require ($caught -and -not(Test-Path (Join-Path $root 'wrong-signer'))) 'Untrusted signer was accepted.'}finally{$wrong.Dispose()}}
Check 'existing candidate is immutable' {$path=Join-Path $root 'None/release.json';$hash=Get-FrameworkFileHash $path;$caught=$false;try{Publish None (Join-Path $root 'None')}catch [IO.InvalidDataException]{$caught=$true};Require ($caught -and (Get-FrameworkFileHash $path) -ceq $hash) 'Existing output was modified.'}
Check 'source payload and signed files remain unchanged' {$after=Get-FrameworkInventory $source;Require (($before|ConvertTo-Json -Depth 20 -Compress) -ceq ($after|ConvertTo-Json -Depth 20 -Compress)) 'Original signed source changed.'}
$key.Dispose();$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_FAULT_RECEIPT $root";if($failed.Count){throw "D2_FAULT_FAILED $($failed.Count)/$($records.Count)"}
