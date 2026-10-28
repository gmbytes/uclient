[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][0-9]+$')][string] $Task,
    [ValidateSet('EditMode', 'PlayMode')][string] $Mode = 'PlayMode',
    [string] $UnityEditor
)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($Task -eq 'N3' -and $Mode -eq 'PlayMode') {
    & dotnet build (Join-Path $projectRoot 'Tests/Fixtures/LoginServer/LoginServer.csproj') -o (Join-Path $projectRoot 'Builds/Fixtures/LoginServer') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'N3 LoginServer fixture build failed.' }
}
$editor = Resolve-UnityEditor $UnityEditor
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$output = Join-Path $projectRoot "Builds/Tests/$Task/$runId"
New-Item -ItemType Directory -Path $output | Out-Null
$xmlPath = Join-Path $output 'results.xml'
$unityArguments = @('-batchmode', '-nographics', '-projectPath', $projectRoot, '-runTests', '-testPlatform', $Mode, '-testCategory', $Task, '-testResults', $xmlPath, '-logFile', (Join-Path $output 'Editor.log'))
$exitCode = Invoke-Unity $editor $unityArguments
$passed = $false
$total = 0
$failed = 0
if (Test-Path -LiteralPath $xmlPath) {
    [xml] $results = Get-Content -LiteralPath $xmlPath -Raw
    $run = $results.'test-run'
    $total = [int] $run.total
    $failed = [int] $run.failed
    $passed = $exitCode -eq 0 -and $total -gt 0 -and $failed -eq 0 -and $run.result -eq 'Passed' -and [int]$run.passed -eq $total
}
Write-RunJson (Join-Path $output 'result.json') ([ordered]@{ task=$Task; mode=$Mode; editor=$editor; arguments=$unityArguments; exitCode=$exitCode; total=$total; failed=$failed; passed=$passed; results=$xmlPath })
Write-Host "Evidence: $output"
if (-not $passed) { Write-Host "TASK_FAIL $Task"; exit 1 }
Write-Host "TASK_PASS $Task"
