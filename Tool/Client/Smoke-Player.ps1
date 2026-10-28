[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Windows64', 'Android')][string] $Platform,
    [string] $UnityEditor
)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = Resolve-UnityEditor $UnityEditor
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot "Builds/Player/$Platform/$runId"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$output = Join-Path $runRoot 'Player'
$target = if ($Platform -eq 'Android') { 'Android' } else { 'Win64' }
$unityArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $projectRoot, '-buildTarget', $target, '-executeMethod', 'Client.Editor.Build.SmokeBuild.Run', '-smokePlatform', $Platform, '-smokeOutput', $output, '-logFile', (Join-Path $runRoot 'Editor.log'))
$exitCode = Invoke-Unity $editor $unityArguments
$buildResult = Join-Path $output 'build-result.json'
$passed = $false
if (Test-Path -LiteralPath $buildResult) {
    $build = Get-Content -LiteralPath $buildResult -Raw | ConvertFrom-Json
    $passed = $exitCode -eq 0 -and $build.result -eq 'Succeeded' -and $build.errors -eq 0 -and $build.backend -eq 'IL2CPP' -and (Test-Path -LiteralPath $build.artifact -PathType Leaf)
}
Write-RunJson (Join-Path $runRoot 'result.json') ([ordered]@{ platform=$Platform; editor=$editor; arguments=$unityArguments; exitCode=$exitCode; passed=$passed; buildResult=$buildResult })
Write-Host "Evidence: $runRoot"
if (-not $passed) { Write-Host "SMOKE_FAIL $Platform"; exit 1 }
Write-Host "SMOKE_PASS $Platform"
