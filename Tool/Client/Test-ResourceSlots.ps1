[CmdletBinding()]
param(
    [ValidateSet('Windows64')][string] $Platform = 'Windows64',
    [string] $UnityEditor
)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = Resolve-UnityEditor $UnityEditor
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot "Builds/ResourceSlots/$runId"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$output = Join-Path $runRoot 'Proof'
$unityArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $projectRoot, '-buildTarget', 'Win64', '-executeMethod', 'Client.Editor.Build.ResourceSlotProofBuilder.Run', '-resourceProofOutput', $output, '-logFile', (Join-Path $runRoot 'Editor.log'))
$exitCode = Invoke-Unity $editor $unityArguments
$passed = $false
$runs = @()
function Get-Snapshot([string] $Root) {
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
if ($exitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $output 'build-result.json'))) {
    $build = Get-Content -LiteralPath (Join-Path $output 'build-result.json') -Raw | ConvertFrom-Json
    if ($build.result -ne 'Succeeded' -or $build.errors -ne 0 -or $build.backend -ne 'IL2CPP') { throw 'Invalid proof build result.' }
    $exe = Join-Path $output 'Player/Client.exe'
    $assembly = Join-Path $output 'Player/GameAssembly.dll'
    $exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    $snapshotA = Get-Snapshot (Join-Path $output 'Slots/A/Resources')
    $snapshotB = Get-Snapshot (Join-Path $output 'Slots/B/Resources')
    $scratchA = Get-Snapshot (Join-Path $output 'Slots/A/Scratch')
    $scratchB = Get-Snapshot (Join-Path $output 'Slots/B/Scratch')
    Write-RunJson (Join-Path $runRoot 'snapshots-before.json') ([ordered]@{ A=$snapshotA; B=$snapshotB; scratchA=$scratchA; scratchB=$scratchB })
    foreach ($marker in @('A','B')) {
        $root = Join-Path $output "Slots/$marker"
        $result = Join-Path $runRoot "Player-$marker.json"
        $log = Join-Path $runRoot "Player-$marker.log"
        $arguments = @('-batchmode','-nographics','-slotRoot',$root,'-slotExpected',$marker,'-slotResult',$result,'-logFile',$log)
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        $natural = $process.WaitForExit(60000)
        if (-not $natural) { $process.Kill(); $process.WaitForExit() }
        $observation = if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw | ConvertFrom-Json } else { $null }
        $runPassed = $natural -and $process.ExitCode -eq 0 -and $null -ne $observation -and $observation.passed -and $observation.lateMutationRejected -and $observation.secondActivationRejected -and $observation.marker -eq $marker -and $observation.mode -eq 'Package' -and $observation.agentCount -eq 3 -and [IO.Path]::GetFullPath($observation.readOnlyPath) -eq [IO.Path]::GetFullPath((Join-Path $root 'Resources')) -and [IO.Path]::GetFullPath($observation.readWritePath) -eq [IO.Path]::GetFullPath((Join-Path $root 'Scratch'))
        $runs += [ordered]@{ marker=$marker; processId=$process.Id; arguments=$arguments; naturalExit=$natural; exitCode=$process.ExitCode; passed=$runPassed; executableHash=(Get-FileHash -LiteralPath $exe).Hash; gameAssemblyHash=(Get-FileHash -LiteralPath $assembly).Hash; result=$result; log=$log }
        $currentA = Get-Snapshot (Join-Path $output 'Slots/A/Resources')
        $currentB = Get-Snapshot (Join-Path $output 'Slots/B/Resources')
        $currentScratchA = Get-Snapshot (Join-Path $output 'Slots/A/Scratch')
        $currentScratchB = Get-Snapshot (Join-Path $output 'Slots/B/Scratch')
        Write-RunJson (Join-Path $runRoot "snapshots-after-$marker.json") ([ordered]@{ A=$currentA; B=$currentB; scratchA=$currentScratchA; scratchB=$currentScratchB })
        if (($currentA | ConvertTo-Json -Compress) -cne ($snapshotA | ConvertTo-Json -Compress) -or ($currentB | ConvertTo-Json -Compress) -cne ($snapshotB | ConvertTo-Json -Compress)) { throw 'Resource snapshot changed during proof.' }
        if (($marker -eq 'A' -and ($currentScratchB | ConvertTo-Json -Compress) -cne ($scratchB | ConvertTo-Json -Compress)) -or ($marker -eq 'B' -and ($currentScratchA | ConvertTo-Json -Compress) -cne ($scratchA | ConvertTo-Json -Compress))) { throw 'Inactive slot Scratch changed during proof.' }
        $scratchA = $currentScratchA
        $scratchB = $currentScratchB
    }
    $passed = @($runs | Where-Object { -not $_.passed }).Count -eq 0 -and $runs.Count -eq 2 -and $runs[0].processId -ne $runs[1].processId -and @($runs | Where-Object { $_.executableHash -ne $exeHash -or $_.gameAssemblyHash -ne $assemblyHash }).Count -eq 0
}
Write-RunJson (Join-Path $runRoot 'result.json') ([ordered]@{ platform=$Platform; editor=$editor; arguments=$unityArguments; exitCode=$exitCode; passed=$passed; runs=$runs })
Write-Host "Evidence: $runRoot"
if (-not $passed) { Write-Host 'RESOURCE_SLOT_FAIL'; exit 1 }
Write-Host 'RESOURCE_SLOT_PASS A B'
