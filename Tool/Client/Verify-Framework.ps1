#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Android','Windows64')][string]$Platform,
    [string]$DeviceId,
    [Parameter(Mandatory)][string]$PlayerArchive,
    [string]$BuildResult,
    [string]$EvidenceFile,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Framework-Evidence.ps1"
. "$PSScriptRoot/Framework-IO.ps1"
. "$PSScriptRoot/Framework-NativeAdapter.ps1"
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $OutputDirectory){$OutputDirectory=Join-Path $project ('Builds/Framework/'+$Platform+'/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=New-FrameworkOutputDirectory $OutputDirectory
$evidence=@{runId='pending';platform=$Platform;deviceId=$DeviceId;cases=@();evidenceKind='Native'}
$inputError=$null
try{
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($BuildResult)) 'E_BUILD_RESULT' 'Supply the exact successful D1 build-result.json; output directories are never guessed.'
    $archive=Read-FrameworkArchive $PlayerArchive $BuildResult $Platform
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($EvidenceFile)) 'E_NATIVE_NOT_RUN' 'Actual native matrix evidence is pending. This preparation command never launches a Player, server, Unity or device.'
    $evidence=Import-FrameworkEvidence $EvidenceFile $archive $Platform $DeviceId
}catch{$inputError=$_.Exception.Message}
$result=Get-FrameworkAcceptance $evidence
if($inputError){$result.errors=@($inputError)+@($result.errors);$result.passed=$false;$result.criteriaAccepted=$false;$result.status='Fail'}
$result.playerArchive=[IO.Path]::GetFullPath($PlayerArchive);$result.buildResult=$BuildResult;$result.evidenceFile=$EvidenceFile
$result.execution=@{mode='EvidenceOnly';nativeProcessesStarted=0;unityProcessesStarted=0;deviceCommands=0;serverProcessesStarted=0}
$result.pending=@('Final D1 native archive approval','Controller release and actual platform adapter qualification','Actual native fault/UI/session matrix','Delivery ThrowOnStart Unity compilation and native execution','Independent D2 review')
Write-FrameworkJsonNew (Join-Path $output 'framework-result.json') $result
Write-FrameworkJsonNew (Join-Path $output 'task-evidence-map.json') @($result.cases|Select-Object taskId,caseId,status,evidence,errors)
Write-Host "FRAMEWORK_EVIDENCE $output"
if($result.passed){Write-Host 'FRAMEWORK_ACCEPTANCE_PASS';exit 0}
Write-Host 'FRAMEWORK_ACCEPTANCE_FAIL';exit 1
