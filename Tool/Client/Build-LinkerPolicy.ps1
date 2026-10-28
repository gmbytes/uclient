[CmdletBinding()]
param([string]$UnityEditor)
. "$PSScriptRoot/Unity-Common.ps1"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = Resolve-UnityEditor $UnityEditor
$editorData = Join-Path (Split-Path -Parent $editor) 'Data'
$deploy = Join-Path $editorData 'il2cpp/build/deploy'
$runtime = Join-Path $editorData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$source = Join-Path $PSScriptRoot 'Linker/DisableSymbolsStep.cs'
$output = Join-Path $projectRoot 'Builds/LinkerPolicy'
New-Item -ItemType Directory -Force $output | Out-Null
$helper = Join-Path $output 'Client.DisableLinkerSymbols.dll'
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$csc = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'Roslyn/bincore/csc.dll' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if(-not $csc -or -not (Test-Path -LiteralPath $runtime)) { throw 'Pinned Unity .NET6 runtime and a C# compiler are required.' }
$compilerArguments = @($csc,'/nologo','/noconfig','/nostdlib+','/target:library','/deterministic+','/optimize+','/debug-',"/out:$helper","/reference:$runtime/System.Private.CoreLib.dll","/reference:$runtime/System.Runtime.dll","/reference:$runtime/System.Console.dll","/reference:$deploy/monolinker.dll","/reference:$deploy/Mono.Cecil.dll",$source)
& dotnet @compilerArguments *> (Join-Path $output 'compiler.log')
$code = $LASTEXITCODE
if($code -ne 0) { Get-Content -LiteralPath (Join-Path $output 'compiler.log'); throw "Linker helper compiler exit $code" }
Write-RunJson (Join-Path $output 'policy.json') ([ordered]@{
    schemaVersion=1; helperPath=$helper; sourcePath=$source
    helperSha256=(Get-FileHash -LiteralPath $helper).Hash.ToLowerInvariant()
    sourceSha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
    monoLinkerSha256=(Get-FileHash -LiteralPath (Join-Path $deploy 'monolinker.dll')).Hash.ToLowerInvariant()
    unityLinkerSha256=(Get-FileHash -LiteralPath (Join-Path $deploy 'UnityLinker.dll')).Hash.ToLowerInvariant()
    compilerPath=$csc; compilerSha256=(Get-FileHash -LiteralPath $csc).Hash.ToLowerInvariant()
    compilerExitCode=$code; compilerArguments=$compilerArguments
})
Write-Host "LINKER_POLICY_BUILT $helper"
