#requires -Version 7.0
# Offline fixture publishing only. Actual rejection must be proved by the native Player/GF path.
function Write-FrameworkSignedEnvelope([string]$Directory,[string]$Name,$Document,[Security.Cryptography.RSA]$SignatureProvider) {
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Document|ConvertTo-Json -Depth 100 -Compress))
    [IO.File]::WriteAllBytes((Join-Path $Directory ($Name+'.json')),$bytes)
    [IO.File]::WriteAllBytes((Join-Path $Directory ($Name+'.sig')),$SignatureProvider.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
}
function New-FrameworkTestRelease {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Windows64','Android')][string]$Platform,
        [Parameter(Mandatory)][string]$PlayerArchive,[Parameter(Mandatory)][string]$BuildResult,
        [Parameter(Mandatory)][string]$SourceReleaseDirectory,[Parameter(Mandatory)][string]$PublicProfile,
        [Parameter(Mandatory)][string]$SigningKey,[Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$CaseId,[Parameter(Mandatory)][string]$ReleaseId,
        [ValidateSet('None','bad-signature','bad-hash','wrong-player-build','wrong-architecture','missing-dll','missing-bundle','tampered-version-table','semantic-version-table')][string]$Fault='None',
        [string]$KnownGoodManifest,[Parameter(Mandatory)][string]$OutputDirectory)
    foreach($identifier in @($RunId,$CaseId,$ReleaseId)){Require-FrameworkFact ($identifier -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_FIXTURE_ID' 'Explicit bounded run/case/release identifiers are required.'}
    $archive=Read-FrameworkArchive $PlayerArchive $BuildResult $Platform
    $sourcePath=[IO.Path]::GetFullPath($SourceReleaseDirectory);$outputPath=[IO.Path]::GetFullPath($OutputDirectory);$keyPath=[IO.Path]::GetFullPath($SigningKey)
    foreach($rootPath in @($sourcePath,[IO.Path]::GetFullPath($PlayerArchive))){
        Require-FrameworkFact (-not $outputPath.Equals($rootPath,[StringComparison]::OrdinalIgnoreCase) -and -not $outputPath.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and -not $rootPath.StartsWith($outputPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_FIXTURE_PATH' 'New fixture output must not overlap original source/archive.'
    }
    foreach($rootPath in @($sourcePath,[IO.Path]::GetFullPath($PlayerArchive),$outputPath)){
        Require-FrameworkFact (-not $keyPath.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_FIXTURE_KEY_PATH' 'Private signer must stay outside source, archive and fixture output.'
    }
    $pinnedProfile=Join-Path $PlayerArchive 'Inputs/Assets/Tests/Generated/D1/NativeProfile.json'
    Require-FrameworkFact ((Get-FrameworkFileHash $PublicProfile) -ceq (Get-FrameworkFileHash $pinnedProfile)) 'E_FIXTURE_PUBLIC_PROFILE' 'Public trust profile differs from the archived native input.'
    $profile=Read-FrameworkJson $PublicProfile;$signatureProvider=[Security.Cryptography.RSA]::Create()
    try{
        $signatureProvider.FromXmlString([IO.File]::ReadAllText($keyPath));$public=$signatureProvider.ExportParameters($false)
        Require-FrameworkFact ($signatureProvider.KeySize -eq 2048 -and [Convert]::ToBase64String($public.Modulus) -ceq $profile.modulus -and [Convert]::ToBase64String($public.Exponent) -ceq $profile.exponent) 'E_FIXTURE_SIGNER' 'Signer does not match the independently archived public pin.'
        foreach($name in @('release','channel')){
            $raw=[IO.File]::ReadAllBytes((Join-Path $sourcePath ($name+'.json')))
            Require-FrameworkFact ($signatureProvider.VerifyData($raw,[IO.File]::ReadAllBytes((Join-Path $sourcePath ($name+'.sig'))),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'E_FIXTURE_SOURCE_SIGNATURE' 'Original signed source authentication failed.'
        }
        $manifest=Read-FrameworkJson (Join-Path $sourcePath 'release.json');$channel=Read-FrameworkJson (Join-Path $sourcePath 'channel.json')
        Require-FrameworkFact ($manifest.schemaVersion -eq 1 -and $channel.schemaVersion -eq 1 -and $manifest.keyId -ceq $profile.keyId -and $channel.keyId -ceq $profile.keyId -and $channel.releaseId -ceq $manifest.releaseId -and $channel.manifestRelativePath -ceq 'release.json' -and $channel.manifestSha256 -ceq (Get-FrameworkFileHash (Join-Path $sourcePath 'release.json'))) 'E_FIXTURE_SOURCE_CHANNEL' 'Original channel does not pin its authenticated manifest.'
        foreach($field in @('playerBuildId','platform','architecture','unityVersion','contractVersion','envelopeSha256','aotMetadataSetId')){Require-FrameworkFact ($manifest.player[$field] -ceq $archive.identity[$field]) 'E_FIXTURE_SOURCE_IDENTITY' 'Source template belongs to a different original Player.'}
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($file in $manifest.files){Require-FrameworkFact ($seen.Add($file.path)) 'E_FIXTURE_DUPLICATE' 'Source template contains duplicate paths.';$path=Resolve-FrameworkReference $sourcePath @{path=$file.path;sha256=$file.sha256};Require-FrameworkFact ((Get-Item -LiteralPath $path).Length -eq $file.length) 'E_FIXTURE_SOURCE_LENGTH' 'Original template file length changed.'}
        foreach($metadata in $archive.archive.metadata){$match=@($manifest.files|Where-Object path -ceq ('Metadata/'+$metadata.path));Require-FrameworkFact ($match.Count -eq 1 -and $match[0].sha256 -ceq $metadata.sha256) 'E_FIXTURE_METADATA' 'Source must preserve every original post-strip metadata image.'}
        foreach($facade in $archive.archive.facades){$match=@($manifest.files|Where-Object path -ceq ('References/'+$facade.path));Require-FrameworkFact ($match.Count -eq 1 -and $match[0].sha256 -ceq $facade.sha256) 'E_FIXTURE_FACADE' 'Source must preserve original facade references.'}
        $game=@($manifest.files|Where-Object path -ceq 'Hotfix/Client.Game.dll');Require-FrameworkFact ($game.Count -eq 1) 'E_FIXTURE_GAME' 'Source Game assembly is required.'
        $table=@($manifest.files|Where-Object path -ceq $manifest.resourceVersionListPath);Require-FrameworkFact ($table.Count -eq 1) 'E_FIXTURE_TABLE' 'Source native table is required.'
        $changedBundle=$null
        if($Fault -ceq 'missing-bundle'){
            $known=Read-FrameworkJson $KnownGoodManifest
            foreach($file in $manifest.files|Where-Object {$_.kind -ceq 'NativeResource' -and $_.path -cne $manifest.resourceVersionListPath}){$old=@($known.files|Where-Object path -ceq $file.path);if($old.Count -eq 0 -or $old[0].sha256 -cne $file.sha256){$changedBundle=$file.path;break}}
            Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($changedBundle)) 'E_FIXTURE_BUNDLE_REUSE' 'Missing-bundle fixture requires a source bundle that known-good would not reuse.'
        }
        $sourceBefore=Get-FrameworkInventory $sourcePath
        $out=New-FrameworkOutputDirectory $outputPath
        foreach($file in $manifest.files){$target=Join-Path $out $file.path;[IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null;[IO.File]::Copy((Join-Path $sourcePath $file.path),$target,$false)}
        $manifest.releaseId=$ReleaseId;$changedPath=$null
        if($Fault -ceq 'wrong-player-build'){$manifest.player.playerBuildId=[Guid]::NewGuid().ToString('N')}
        if($Fault -ceq 'wrong-architecture'){$manifest.player.architecture=if($manifest.player.architecture -ceq 'arm64'){'x64'}else{'arm64'}}
        if($Fault -ceq 'semantic-version-table'){
            $changedPath=$manifest.resourceVersionListPath;$path=Join-Path $out $changedPath
            # 签名/hash 合法但 GF 不能读取的独立候选；是否拒绝由真实 GF 运行证据判定。
            [IO.File]::WriteAllBytes($path,[Text.Encoding]::ASCII.GetBytes('D2-invalid-native-GF-package-table'))
            $table[0].length=(Get-Item $path).Length;$table[0].sha256=Get-FrameworkFileHash $path
        }
        Write-FrameworkSignedEnvelope $out release $manifest $signatureProvider
        Write-FrameworkSignedEnvelope $out channel @{schemaVersion=1;keyId=$profile.keyId;releaseId=$ReleaseId;manifestRelativePath='release.json';manifestSha256=(Get-FrameworkFileHash (Join-Path $out 'release.json'))} $signatureProvider
        if($Fault -ceq 'bad-signature'){$path=Join-Path $out 'release.sig';$bytes=[IO.File]::ReadAllBytes($path);$bytes[0]=$bytes[0] -bxor 1;[IO.File]::WriteAllBytes($path,$bytes)}
        if($Fault -cin @('bad-hash','tampered-version-table')){
            $changedPath=if($Fault -ceq 'bad-hash'){'Hotfix/Client.Game.dll'}else{$manifest.resourceVersionListPath}
            $path=Join-Path $out $changedPath;$bytes=[IO.File]::ReadAllBytes($path);Require-FrameworkFact ($bytes.Length -gt 0) 'E_FIXTURE_TAMPER' 'Tamper target must contain bytes.';$bytes[0]=$bytes[0] -bxor 1;[IO.File]::WriteAllBytes($path,$bytes)
        }
        if($Fault -cin @('missing-dll','missing-bundle')){
            $changedPath=if($Fault -ceq 'missing-dll'){'Hotfix/Client.Game.dll'}else{$changedBundle}
            $path=[IO.Path]::GetFullPath((Join-Path $out $changedPath));Require-FrameworkFact ($path.StartsWith($out+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_FIXTURE_DELETE' 'Fixture deletion must remain within its newly owned output.'
            [IO.File]::Delete($path)
        }
        $sourceAfter=Get-FrameworkInventory $sourcePath
        foreach($name in $sourceBefore.Keys){Require-FrameworkFact ($sourceAfter.ContainsKey($name) -and $sourceAfter[$name].sha256 -ceq $sourceBefore[$name].sha256) 'E_FIXTURE_SOURCE_CHANGED' 'Original signed source changed during fixture publication.'}
        Require-FrameworkFact ($sourceAfter.Count -eq $sourceBefore.Count) 'E_FIXTURE_SOURCE_CHANGED' 'Original source inventory changed.'
        $receipt=@{schemaVersion=1;scope='TestReleaseInput';nativeAcceptance=$false;runId=$RunId;caseId=$CaseId;releaseId=$ReleaseId;fault=$Fault;changedPath=$changedPath;playerBuildId=$archive.playerBuildId;archiveSha256=$archive.archiveSha256;publicProfileSha256=(Get-FrameworkFileHash $PublicProfile);sourceReleaseId=$channel.releaseId;sourceFiles=$sourceBefore;publishedFiles=(Get-FrameworkInventory $out);sourceUnchanged=$true}
        Write-FrameworkJsonNew (Join-Path $out 'fixture-receipt.json') $receipt
        return $receipt
    }finally{$signatureProvider.Dispose()}
}
