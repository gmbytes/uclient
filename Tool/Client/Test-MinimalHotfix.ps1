[CmdletBinding()]
param(
    [ValidateSet('Windows64')][string] $Platform = 'Windows64',
    [string] $UnityEditor
)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = Resolve-UnityEditor $UnityEditor
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot "Builds/MinimalHotfix/$runId"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$output = Join-Path $runRoot 'Proof'
$fixture = Join-Path $projectRoot 'Assets/Scripts/Game/HotfixProofFixture.cs'
if ((Test-Path -LiteralPath $fixture) -or (Test-Path -LiteralPath ($fixture + '.meta'))) { throw 'Temporary proof source already exists; inspect before rerunning.' }
$unityArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $projectRoot, '-buildTarget', 'Win64', '-executeMethod', 'Client.Editor.Build.MinimalHotfixBuilder.Run', '-hotfixProofOutput', $output, '-logFile', (Join-Path $runRoot 'Editor.log'))
$exitCode = -1
try {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixProof/HotfixProofFixture.cs.txt') -Destination $fixture
    $exitCode = Invoke-Unity $editor $unityArguments
}
finally {
    Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($fixture + '.meta') -ErrorAction SilentlyContinue
}
$runs = @()
$passed = $false
function Get-Snapshot([string] $Root) {
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
if ($exitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $output 'build-result.json'))) {
    $build = Get-Content -LiteralPath (Join-Path $output 'build-result.json') -Raw | ConvertFrom-Json
    if ($build.result -ne 'Succeeded' -or $build.errors -ne 0 -or $build.backend -ne 'IL2CPP') { throw 'Invalid proof build result.' }
    $playerRoot = Join-Path $output 'Player'
    $exe = Join-Path $playerRoot 'Client.exe'
    $nativeBefore = Get-Snapshot $playerRoot
    $metadataRoot = Join-Path $output 'Metadata'
    $metadataBefore = Get-Snapshot $metadataRoot
    $playerBuildId = (Get-Content -LiteralPath (Join-Path $metadataRoot 'player-build-id.txt') -Raw).Trim()
    $metadataSource = Get-Content -LiteralPath (Join-Path $metadataRoot 'metadata-source.json') -Raw | ConvertFrom-Json
    if ($metadataSource.playerBuildId -ne $playerBuildId -or $metadataSource.platform -ne 'StandaloneWindows64' -or $metadataSource.buildResult -ne (Join-Path $output 'build-result.json')) { throw 'The metadata archive is not bound to this actual Player build.' }
    foreach ($aot in Get-ChildItem -LiteralPath $metadataRoot -Filter '*.dll' -File) {
        $source = Join-Path $metadataSource.sourceDirectory $aot.Name
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath $aot.FullName -Algorithm SHA256).Hash) { throw "Archived metadata differs from this build's stripped output: $($aot.Name)" }
    }
    Write-RunJson (Join-Path $runRoot 'native-before.json') $nativeBefore
    Write-RunJson (Join-Path $runRoot 'metadata.json') $metadataBefore
    foreach ($version in @('v1','v2')) {
        $expected = if ($version -eq 'v1') { 'proof-v1' } else { 'proof-v2:42' }
        $dllRoot = Join-Path $output "Dlls/$version"
        $dllHash = (Get-FileHash -LiteralPath (Join-Path $dllRoot 'Client.Game.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        $protocolHash = (Get-FileHash -LiteralPath (Join-Path $dllRoot 'Client.Game.Protocol.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        $result = Join-Path $runRoot "Player-$version.json"
        $log = Join-Path $runRoot "Player-$version.log"
        $arguments = @('-batchmode','-nographics','-hotfixRoot',$dllRoot,'-metadataRoot',$metadataRoot,'-hotfixExpected',$expected,'-hotfixResult',$result,'-logFile',$log)
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        $natural = $process.WaitForExit(60000)
        if (-not $natural) { $process.Kill(); $process.WaitForExit() }
        $observation = if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw | ConvertFrom-Json } else { $null }
        $logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
        $otherMarker = if ($version -eq 'v1') { 'HOTFIX_PROOF proof-v2' } else { 'HOTFIX_PROOF proof-v1' }
        $runPassed = $natural -and $process.ExitCode -eq 0 -and $null -ne $observation -and $observation.passed -and $observation.marker -eq $expected -and $observation.readyCount -eq 1 -and $observation.secondLoadRejected -and $observation.metadataCount -gt 0 -and $observation.playerBuildId -eq $playerBuildId -and $observation.dllSha256 -eq $dllHash -and $observation.addedTypePresent -eq ($version -eq 'v2') -and $logText.Contains("HOTFIX_PROOF $expected") -and -not $logText.Contains($otherMarker)
        $runPassed = $runPassed -and $observation.envelopeVerified -and $observation.emptyRegistryVerified -and $observation.protocolDllSha256 -eq $protocolHash -and ($observation.loadOrder -join ',') -ceq 'Client.Game.Protocol,Client.Game' -and $logText.Contains('PROTOCOL_PROOF empty-registry')
        $nativeAfter = Get-Snapshot $playerRoot
        Write-RunJson (Join-Path $runRoot "native-after-$version.json") $nativeAfter
        if (($nativeAfter | ConvertTo-Json -Depth 10 -Compress) -cne ($nativeBefore | ConvertTo-Json -Depth 10 -Compress)) { throw 'The native Player changed between hotfix runs.' }
        if (((Get-Snapshot $metadataRoot) | ConvertTo-Json -Depth 10 -Compress) -cne ($metadataBefore | ConvertTo-Json -Depth 10 -Compress)) { throw 'The archived metadata changed.' }
        $runs += [ordered]@{ version=$version; processId=$process.Id; arguments=$arguments; naturalExit=$natural; exitCode=$process.ExitCode; passed=$runPassed; dllSha256=$dllHash; protocolDllSha256=$protocolHash; result=$result; log=$log }
    }
    $passed = $runs.Count -eq 2 -and @($runs | Where-Object { -not $_.passed }).Count -eq 0 -and $runs[0].processId -ne $runs[1].processId -and $runs[0].dllSha256 -ne $runs[1].dllSha256
}
Write-RunJson (Join-Path $runRoot 'result.json') ([ordered]@{ platform=$Platform; editor=$editor; arguments=$unityArguments; exitCode=$exitCode; passed=$passed; runs=$runs })
Write-Host "Evidence: $runRoot"
if (-not $passed) { Write-Host 'MINIMAL_HOTFIX_FAIL'; exit 1 }
Write-Host 'MINIMAL_HOTFIX_PASS proof-v1 proof-v2:42'
