#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Windows64','Android')][string]$Platform,
    [Parameter(Mandatory)][string]$PlayerArchive,[Parameter(Mandatory)][string]$BuildResult,
    [Parameter(Mandatory)][string]$BuildReceipt,[Parameter(Mandatory)][string]$RuntimePlayerRoot,
    [Parameter(Mandatory)][string]$NativeEvidenceRoot,[Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$OutputDirectory,[string]$DeviceId,[string]$AdbPath,[string]$AdbSha256)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Framework-Evidence.ps1"
. "$PSScriptRoot/Framework-IO.ps1"
. "$PSScriptRoot/Framework-Collector.ps1"
$archive=Read-FrameworkArchive $PlayerArchive $BuildResult $Platform
$executable=if($Platform -ceq 'Windows64'){Join-Path $RuntimePlayerRoot 'Client.exe'}else{$AdbPath}
$plan=New-FrameworkCollectionPlan -Platform $Platform -RunId $RunId -PlayerPath $executable -NativeEvidenceRoot $NativeEvidenceRoot -DeviceId $DeviceId -ApplicationId $archive.profilePackageId
$bound=Bind-FrameworkCollectionPlan $plan $archive $BuildReceipt $RuntimePlayerRoot @{file=$AdbPath;sha256=$AdbSha256}
$output=New-FrameworkOutputDirectory $OutputDirectory
$bound.scope='PreparationOnly';$bound.nativeAcceptance=$false
Write-FrameworkJsonNew (Join-Path $output 'execution-plan.json') $bound
Write-FrameworkJsonNew (Join-Path $output 'required-inputs.json') @{runId=$RunId;platform=$Platform;status='NotRun';nativeAcceptance=$false;required=@('Final D1 archive approval and explicit controller execution release','Fresh unique signed candidate IDs and full original payload-template receipts','Initial task-owned clean installation receipt','Per-case signed setup and recovery input receipts','Owned update/Tok service and operator interaction records','Android per-app Java/process ABI evidence when applicable','Independent adapter qualification and D2 review')}
Write-Host "FRAMEWORK_PREPARATION_PLAN $output (no native processes, device commands or servers executed)"
