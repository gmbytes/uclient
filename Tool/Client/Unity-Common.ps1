Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-UnityEditor([string] $Path) {
    if (-not $Path) { $Path = $env:UNITY_EDITOR_PATH }
    if (-not $Path) { $Path = 'C:\Program Files\Unity 2022.3.25f1\Editor\Unity.exe' }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Unity Editor not found: $Path. Set UNITY_EDITOR_PATH." }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Set-ClientBuildEnvironment([string] $ProjectRoot, [string] $UnityEditor, [string] $HybridClrSource) {
    $projectPath = [IO.Path]::GetFullPath($ProjectRoot)
    $androidPlayer = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($UnityEditor))) 'Data/PlaybackEngines/AndroidPlayer'
    $source = $HybridClrSource
    if ([string]::IsNullOrWhiteSpace($source)) { $source = $env:SGAME_HYBRIDCLR_SOURCE }
    if ([string]::IsNullOrWhiteSpace($source)) { $source = Join-Path $projectPath 'Builds/dependencies/assembled-libil2cpp' }
    if (-not [IO.Path]::IsPathRooted($source)) { $source = Join-Path $projectPath $source }
    $source = [IO.Path]::GetFullPath($source)

    if ([string]::IsNullOrWhiteSpace($env:SGAME_COMM_ROOT)) { $env:SGAME_COMM_ROOT = [IO.Path]::GetFullPath((Join-Path $projectPath '../comm')) }
    $env:SGAME_HYBRIDCLR_SOURCE = $source
    if ([string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) { $env:ANDROID_SDK_ROOT = Join-Path $androidPlayer 'SDK' }
    if ([string]::IsNullOrWhiteSpace($env:ANDROID_NDK_ROOT)) { $env:ANDROID_NDK_ROOT = Join-Path $androidPlayer 'NDK' }
    if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME)) { $env:JAVA_HOME = Join-Path $androidPlayer 'OpenJDK' }
    # SDK_TEST_BASE_URL is an explicit caller override, never an implicit cache in another checkout.
}

function Invoke-Unity([string] $Editor, [string[]] $Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Editor
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    return $process.ExitCode
}

function Write-RunJson([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 20) + "`n").Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
