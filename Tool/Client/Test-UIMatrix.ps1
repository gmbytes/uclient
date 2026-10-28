[CmdletBinding()]
param([ValidateSet('Editor')][string] $Profile='Editor',[string] $UnityEditor)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor=Resolve-UnityEditor $UnityEditor
$runId=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$output=Join-Path $projectRoot "Builds/Tests/U2Matrix/$runId"
New-Item -ItemType Directory -Path $output | Out-Null
$xmlPath=Join-Path $output 'results.xml'
$arguments=@('-batchmode','-projectPath',$projectRoot,'-runTests','-testPlatform','PlayMode','-testCategory','UIMatrix','-testResults',$xmlPath,'-uiMatrixOutput',$output,'-logFile',(Join-Path $output 'Editor.log'))
$exitCode=Invoke-Unity $editor $arguments
$passed=$false; $count=0
if((Test-Path $xmlPath) -and (Test-Path (Join-Path $output 'matrix.json'))) {
    [xml]$tests=Get-Content $xmlPath -Raw
    $matrix=Get-Content (Join-Path $output 'matrix.json') -Raw | ConvertFrom-Json
    $count=@($matrix.cases).Count
    $passed=$exitCode -eq 0 -and $tests.'test-run'.result -eq 'Passed' -and [int]$tests.'test-run'.total -gt 0 -and $matrix.passed -and $count -eq 40
    foreach($case in $matrix.cases) { if(-not(Test-Path (Join-Path $output $case.screenshot))) { $passed=$false } }
}
Write-RunJson (Join-Path $output 'result.json') ([ordered]@{profile=$Profile; editor=$editor; exitCode=$exitCode;caseCount=$count;passed=$passed;evidence=$output;scope='Editor preview only; Android keyboard/rotation remain D2 device validation'})
Write-Host "Evidence: $output"
if(-not $passed) { Write-Host 'U2_MATRIX_FAIL'; exit 1 }
Write-Host 'U2_MATRIX_PASS'
