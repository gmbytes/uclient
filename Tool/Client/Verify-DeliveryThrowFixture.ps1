#requires -Version 7.0
[CmdletBinding()]param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Framework-Evidence.ps1"
. "$PSScriptRoot/Framework-IO.ps1"
$output=New-FrameworkOutputDirectory $OutputDirectory
$result=@{passed=$false;nativeAcceptance=$false;unityCompilation='NotRun'}
try{$result=Assert-FrameworkThrowFixture (Join-Path $PSScriptRoot '../../Tests/Fixtures/Delivery')}catch{$result.error=$_.Exception.Message}
Write-FrameworkJsonNew (Join-Path $output 'throw-fixture-result.json') $result
if(-not $result.passed){exit 1}
Write-Host 'DELIVERY_THROW_SOURCE_PASS (source bytes only; Unity/native NotRun)'
