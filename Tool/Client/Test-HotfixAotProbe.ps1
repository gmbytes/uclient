[CmdletBinding()]
param([string]$UnityEditor)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$commRoot = if($null -ne $env:SGAME_COMM_ROOT) { if([string]::IsNullOrWhiteSpace($env:SGAME_COMM_ROOT)){throw 'SGAME_COMM_ROOT is explicitly empty.'}; [IO.Path]::GetFullPath($env:SGAME_COMM_ROOT) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot '../comm')) }
if(-not (Test-Path -LiteralPath (Join-Path $commRoot 'proto/protocol.lock.json'))) { throw 'A valid shared comm root is required; set SGAME_COMM_ROOT for an isolated worktree.' }
$generated = Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/Generated'
$manifest = Get-Content -LiteralPath (Join-Path $generated 'Pristine/protocol-manifest.json') -Raw | ConvertFrom-Json
foreach($property in $manifest.generated_sha256.PSObject.Properties) { if((Get-FileHash -LiteralPath (Join-Path (Join-Path $generated 'Pristine') $property.Name)).Hash.ToLowerInvariant() -cne $property.Value) {throw 'Pristine generated fixture differs from the generator manifest.'} }
foreach($property in $manifest.source_sha256.PSObject.Properties) {
    $source = if($property.Name -eq 'tok/client/v1/client.proto') {Join-Path $commRoot ('proto/'+$property.Name)} else {Join-Path $commRoot ('proto/tests/fixtures/Protocols/F6AotProbe/'+$property.Name)}
    if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -cne $property.Value){throw 'Shared F6 fixture source differs; regenerate through the shared generator.'}
}
$pristineRegistry = [IO.File]::ReadAllText((Join-Path $generated 'Pristine/GameMessageRegistry.cs'))
if(($pristineRegistry.Split('namespace Client.Game.Protocol').Length-1)-ne1 -or [IO.File]::ReadAllText((Join-Path $generated 'AdaptedRegistry.cs.txt')) -cne $pristineRegistry.Replace('namespace Client.Game.Protocol','namespace Client.Game.Protocol.F6Probe.Generated')) {throw 'F6 registry adaptation must change exactly one namespace and nothing else.'}
$editor = Resolve-UnityEditor $UnityEditor
& "$PSScriptRoot/Build-LinkerPolicy.ps1" -UnityEditor $editor
$linkerPolicy = Join-Path $projectRoot 'Builds/LinkerPolicy/policy.json'
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$runRoot = Join-Path $projectRoot "Builds/F6AotProbe/$runId"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$key = [Security.Cryptography.RSA]::Create(2048)
$keyPath = Join-Path $runRoot 'ephemeral-test-key.xml'
[IO.File]::WriteAllText($keyPath, $key.ToXmlString($true))
$public = $key.ExportParameters($false)
$runtime = (Get-Content -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/RuntimeProbe.cs.txt') -Raw).Replace('__MODULUS__',[Convert]::ToBase64String($public.Modulus)).Replace('__EXPONENT__',[Convert]::ToBase64String($public.Exponent))
$factory = (Get-Content -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/TestVerifier.cs.txt') -Raw).Replace('__MODULUS__',[Convert]::ToBase64String($public.Modulus)).Replace('__EXPONENT__',[Convert]::ToBase64String($public.Exponent))
$key.Dispose()
$temporary = @('Assets/Scripts/Framework/Bootstrap/F6RuntimeProbe.cs','Assets/Scripts/Game/F6Entry.cs','Assets/Scripts/Game/Protocol/F6ProtocolProbe.cs','Assets/Scripts/Framework/HotUpdate/F6TestVerifier.cs','Assets/Scripts/Game/Protocol/F6GeneratedMessage.cs','Assets/Scripts/Game/Protocol/F6GeneratedRegistry.cs')
foreach($path in $temporary) { if(Test-Path -LiteralPath (Join-Path $projectRoot $path)) { throw "Temporary F6 fixture exists: $path" } }
$asmdefs = @('Assets/Scripts/Game/Client.Game.asmdef','Assets/Scripts/Game/Protocol/Client.Game.Protocol.asmdef')
$restored = $asmdefs + @('Assets/Scenes/Bootstrap.unity','Assets/Prefabs/App/UIUpdate.prefab','Assets/Prefabs/App/GameRoot.prefab','Assets/Prefabs/App/GameFramework.prefab','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/HybridCLRSettings.asset','ProjectSettings/ProjectSettings.asset','Assets/Scripts/Framework/HotUpdate/Generated/link.xml','Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs')
$original = @{}
foreach($path in $restored) { $original[$path] = [IO.File]::ReadAllBytes((Join-Path $projectRoot $path)) }
$baselinePaths = @('Assets/StreamingAssets/F6Baseline','Assets/StreamingAssets/F6PlayerIdentity.json')
foreach($path in $baselinePaths) { if(Test-Path -LiteralPath (Join-Path $projectRoot $path)) { throw "Generated F6 baseline already exists; inspect before rerun: $path" } }
$output = Join-Path $runRoot 'Proof'
$arguments = @('-batchmode','-nographics','-quit','-projectPath',$projectRoot,'-buildTarget','Win64','-executeMethod','Client.Editor.Build.HotfixAotProbeBuilder.Run','-f6Output',$output,'-f6Key',$keyPath,'-f6LinkerPolicy',$linkerPolicy,'-hybridclrSource','E:/sgame/client/Builds/dependencies/assembled-libil2cpp','-logFile',(Join-Path $runRoot 'Editor.log'))
$exitCode = -1
try {
    [IO.File]::WriteAllText((Join-Path $projectRoot $temporary[0]), $runtime)
    [IO.File]::WriteAllText((Join-Path $projectRoot $temporary[3]), $factory)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/Entry.cs.txt') -Destination (Join-Path $projectRoot $temporary[1])
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Fixtures/HotfixAotProbe/ProtocolProbe.cs.txt') -Destination (Join-Path $projectRoot $temporary[2])
    Copy-Item -LiteralPath (Join-Path $generated 'Pristine/AotProbe.cs') -Destination (Join-Path $projectRoot $temporary[4])
    Copy-Item -LiteralPath (Join-Path $generated 'AdaptedRegistry.cs.txt') -Destination (Join-Path $projectRoot $temporary[5])
    foreach($path in $asmdefs) { $absolute = Join-Path $projectRoot $path; $json = Get-Content -LiteralPath $absolute -Raw | ConvertFrom-Json; $json.references += 'Client.Protocol.Envelope'; $json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $absolute -Encoding utf8 }
    $exitCode = Invoke-Unity $editor $arguments
}
finally {
    foreach($path in $temporary) { Remove-Item -LiteralPath (Join-Path $projectRoot $path) -ErrorAction SilentlyContinue; Remove-Item -LiteralPath (Join-Path $projectRoot ($path+'.meta')) -ErrorAction SilentlyContinue }
    foreach($path in $restored) { [IO.File]::WriteAllBytes((Join-Path $projectRoot $path), $original[$path]) }
    foreach($path in $baselinePaths) {
        $absolute = [IO.Path]::GetFullPath((Join-Path $projectRoot $path))
        $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Assets/StreamingAssets')) + [IO.Path]::DirectorySeparatorChar
        if(-not $absolute.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe generated baseline cleanup target.' }
        if(Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
        Remove-Item -LiteralPath ($absolute+'.meta') -ErrorAction SilentlyContinue
    }
}
$passed = $false; $playerExit = -1
if($exitCode -eq 0) {
    $build = Get-Content -LiteralPath (Join-Path $output 'build-result.json') -Raw | ConvertFrom-Json
    if($build.result -ne 'Succeeded' -or $build.errors -ne 0 -or $build.backend -ne 'IL2CPP') { throw 'Not a successful Release IL2CPP build.' }
    $start = [Diagnostics.ProcessStartInfo]::new(); $start.FileName = Join-Path $output 'Player/Client.exe'; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $playerArgs = @('-batchmode','-nographics','-f6Store',(Join-Path $runRoot 'Installed'),'-f6Result',(Join-Path $runRoot 'Player.json'),'-logFile',(Join-Path $runRoot 'Player.log'))
    foreach($a in $playerArgs){ $start.ArgumentList.Add($a) }
    $process = [Diagnostics.Process]::Start($start)
    $natural = $process.WaitForExit(60000)
    if(-not $natural){ $process.Kill(); $process.WaitForExit() }
    $playerExit = $process.ExitCode
    if(Test-Path -LiteralPath (Join-Path $runRoot 'Player.json')) {
        $result = Get-Content -LiteralPath (Join-Path $runRoot 'Player.json') -Raw | ConvertFrom-Json
        $archive = Get-Content -LiteralPath (Join-Path $output 'Archive/archive.json') -Raw | ConvertFrom-Json
        $passed = $natural -and $playerExit -eq 0 -and $result.passed -and $result.secondLoadRejected -and $result.resourcesInitialized -and $result.probeCompletionCount -eq 1 -and $result.metadataCount -eq $archive.metadata.Count -and $result.playerBuildId -eq $archive.playerBuildId -and $result.metadataSetId -eq $archive.aotMetadataSetId -and $result.probes -eq 'list-hot-struct,dictionary-imessage,async-task,hot-repeated,hot-map,registered-hot-parser,unknown-fields,empty-registry'
        foreach($f in $archive.player) { if((Get-FileHash -LiteralPath (Join-Path (Join-Path $output 'Player') $f.path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $f.sha256) { throw 'Native Player changed during probe.' } }
    }
}
Write-RunJson (Join-Path $runRoot 'result.json') ([ordered]@{editor=$editor;arguments=$arguments;exitCode=$exitCode;playerExitCode=$playerExit;passed=$passed})
Write-Host "Evidence: $runRoot"
if(-not $passed){ Write-Host 'F6_AOT_PROBE_FAIL'; exit 1 }
Write-Host 'F6_AOT_PROBE_PASS'
