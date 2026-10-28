Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-UnityEditor([string] $Path) {
    if (-not $Path) { $Path = $env:UNITY_EDITOR_PATH }
    if (-not $Path) { $Path = 'C:\Program Files\Unity 2022.3.25f1\Editor\Unity.exe' }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Unity Editor not found: $Path. Set UNITY_EDITOR_PATH." }
    return (Resolve-Path -LiteralPath $Path).Path
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
