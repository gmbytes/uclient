#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Player','Patch')][string]$Kind,
    [Parameter(Mandatory)][ValidateSet('Android','Windows64')][string]$Platform,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$PlayerArchive,
    [string]$PublicProfile=$env:SGAME_PUBLIC_PROFILE_FILE,
    [string]$SigningKey=$env:SGAME_SIGNING_KEY_FILE,
    [string]$LinkerPolicy,
    [string]$ReleaseId,
    [string]$HotfixOverlay,
    [string]$UnityEditor,
    [string]$ProtocSource,
    [string]$HybridClrSource
)
. "$PSScriptRoot/Unity-Common.ps1"
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Full([string]$Path){if([IO.Path]::IsPathRooted($Path)){[IO.Path]::GetFullPath($Path)}else{[IO.Path]::GetFullPath((Join-Path $project $Path))}}
function NoReparse([string]$Path){for($entry=[IO.Path]::GetFullPath($Path);$entry;$entry=[IO.Path]::GetDirectoryName($entry)){if(Test-Path -LiteralPath $entry){if(((Get-Item -LiteralPath $entry -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw "Build paths cannot traverse reparse points: $entry"}}}}
function IsWithin([string]$Path,[string]$Root){$Path.Equals($Root,[StringComparison]::OrdinalIgnoreCase)-or$Path.StartsWith($Root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}
if($Kind-eq'Patch'-and[string]::IsNullOrWhiteSpace($PlayerArchive)){throw 'Patch requires an explicit PlayerArchive.'}
if($Kind-eq'Player'-and-not[string]::IsNullOrWhiteSpace($PlayerArchive)){throw 'PlayerArchive is only valid for Patch.'}
if([string]::IsNullOrWhiteSpace($SigningKey)){throw 'SigningKey or SGAME_SIGNING_KEY_FILE must identify an external private signer.'}
if($Kind-eq'Player'-and[string]::IsNullOrWhiteSpace($PublicProfile)){throw 'Player requires an independently trusted PublicProfile.'}
$OutputRoot=Full $OutputRoot;NoReparse $OutputRoot
$SigningKey=Full $SigningKey;NoReparse $SigningKey
foreach($source in @('Assets','Packages','ProjectSettings','Tool','Tests','docs')){if(IsWithin $OutputRoot (Join-Path $project $source)){throw 'OutputRoot cannot be a source directory.'};if(IsWithin $SigningKey (Join-Path $project $source)){throw 'Private signer cannot be a source/native/package input.'}}
if(-not(Test-Path -LiteralPath $SigningKey -PathType Leaf)){throw 'SigningKey does not exist.'}
if($PublicProfile){$PublicProfile=Full $PublicProfile;NoReparse $PublicProfile;if(-not(Test-Path -LiteralPath $PublicProfile -PathType Leaf)){throw 'PublicProfile does not exist.'}}
if($PlayerArchive){$PlayerArchive=Full $PlayerArchive;NoReparse $PlayerArchive;if(IsWithin $OutputRoot $PlayerArchive){throw 'Patch output cannot be inside its immutable archive.'};if(IsWithin $SigningKey $PlayerArchive){throw 'Private signer cannot be inside the Player archive.'};if(-not(Test-Path -LiteralPath (Join-Path $PlayerArchive 'archive.json'))){throw 'PlayerArchive is not finalized.'}}
if($HotfixOverlay){$HotfixOverlay=Full $HotfixOverlay;NoReparse $HotfixOverlay}
if(-not$ReleaseId){$ReleaseId=if($Kind-eq'Player'){'v1'}else{'v2'}}
$editor=Resolve-UnityEditor $UnityEditor
Set-ClientBuildEnvironment -ProjectRoot $project -UnityEditor $editor -HybridClrSource $HybridClrSource
$protoc=Join-Path $project 'Builds/tools/protoc/36.2/bin/protoc.exe';NoReparse $protoc
$expected=(Get-Content (Join-Path $project 'Tests/Fixtures/Delivery/provenance.json') -Raw|ConvertFrom-Json).protocSha256
if(-not(Test-Path -LiteralPath $protoc)){if([string]::IsNullOrWhiteSpace($ProtocSource)){throw "Pinned protoc is missing. Run '$env:SGAME_COMM_ROOT/tools/Dependencies/Restore-Dependencies.ps1 -ClientRoot $project' or supply an explicit pinned -ProtocSource."};if((Get-FileHash -LiteralPath $ProtocSource -Algorithm SHA256).Hash.ToLowerInvariant()-cne$expected){throw 'Pinned protoc source bytes differ.'};New-Item -ItemType Directory -Force (Split-Path -Parent $protoc)|Out-Null;Copy-Item -LiteralPath $ProtocSource -Destination $protoc}
if((Get-FileHash -LiteralPath $protoc -Algorithm SHA256).Hash.ToLowerInvariant()-cne$expected){throw 'Owned protoc cache differs from the pinned input.'}
if(-not$LinkerPolicy){& "$PSScriptRoot/Build-LinkerPolicy.ps1" -UnityEditor $editor;if($LASTEXITCODE-ne0){throw 'Linker policy producer failed.'};$LinkerPolicy=Join-Path $project 'Builds/LinkerPolicy/policy.json'}else{$LinkerPolicy=Full $LinkerPolicy}
$invocation=Join-Path $project ('Builds/BuildInvocations/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8));New-Item -ItemType Directory -Path $invocation|Out-Null
$requestFile=Join-Path $invocation 'request.json';$receipt=Join-Path $invocation 'receipt.json'
Write-RunJson (Join-Path $invocation 'toolchain.json') ([ordered]@{commRoot=$env:SGAME_COMM_ROOT;hybridClrSource=$env:SGAME_HYBRIDCLR_SOURCE;androidSdkRoot=$env:ANDROID_SDK_ROOT;androidNdkRoot=$env:ANDROID_NDK_ROOT;javaHome=$env:JAVA_HOME;sdkMetadataBaseUrl=$env:SDK_TEST_BASE_URL})
Write-RunJson $requestFile ([ordered]@{Kind=$Kind;Platform=$Platform;OutputRoot=$OutputRoot;PlayerArchive=$PlayerArchive;PublicProfilePath=$PublicProfile;SigningKeyPath=$SigningKey;LinkerPolicyPath=$LinkerPolicy;ReleaseId=$ReleaseId;HotfixOverlay=$HotfixOverlay})
$arguments=@('-batchmode','-nographics','-quit','-projectPath',$project,'-buildTarget',$(if($Platform-eq'Android'){'Android'}else{'Win64'}),'-executeMethod','Client.Editor.Build.ClientBuild.Run','-clientBuildRequest',$requestFile,'-clientBuildReceipt',$receipt,'-logFile',(Join-Path $invocation 'Editor.log'))
$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$editor;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::Start($start)
Write-RunJson (Join-Path $invocation 'process.json') ([ordered]@{pid=$process.Id;editor=$editor;arguments=$arguments;startedUtc=[DateTime]::UtcNow.ToString('o')})
Write-Host "CLIENT_BUILD_STARTED pid=$($process.Id) invocation=$invocation"
$process.WaitForExit()
$receiptData=if(Test-Path -LiteralPath $receipt){Get-Content -LiteralPath $receipt -Raw|ConvertFrom-Json}else{$null}
Write-RunJson (Join-Path $invocation 'invocation-result.json') ([ordered]@{pid=$process.Id;processExited=$process.HasExited;exitCode=$process.ExitCode;receipt=$receiptData;finishedUtc=[DateTime]::UtcNow.ToString('o')})
if($null-eq$receiptData){throw "Unity produced no result receipt. Invocation: $invocation"}
$actual=Full $receiptData.OutputDirectory
if(-not(IsWithin $actual $OutputRoot)-or[IO.Path]::GetFileName($actual)-notmatch'^Build_\d{8}T\d{9}Z_[a-f0-9]{12}$'){throw 'Invalid actual output directory in receipt.'}
$resultPath=Full $receiptData.ResultFile
if($resultPath-cne(Join-Path $actual 'build-result.json')){throw 'Receipt result path differs from its actual output.'}
$result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
if($process.ExitCode-ne0-or$result.Status-notin@(1,'Succeeded')){throw "Build failed. Actual result: $resultPath"}
Write-Host "CLIENT_BUILD_SUCCEEDED $resultPath"
$result
