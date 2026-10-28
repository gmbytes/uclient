#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TokRoot,
    [Parameter(Mandatory)][string]$ProtoRoot,
    [switch]$Check,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('../../Builds/Tests/N0/' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Reject([string]$Code, [string]$Message) {
    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['ContractCode'] = $Code
    throw $exception
}

function File-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        Reject 'E_REQUIRED_PATH_MISSING' "Required nonempty file is missing: $Path"
    }
}

function Read-Json([string]$Path, [string]$Code) {
    try { return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable -ErrorAction Stop) }
    catch { Reject $Code "Invalid JSON: $Path" }
}

function Git-Value([string[]]$Arguments) {
    $value = @(& git -C $TokRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0 -or $value.Count -ne 1) { Reject 'E_TOK_GIT' "Cannot read Tok git identity: $($Arguments -join ' ')" }
    return "$($value[0])".Trim()
}

function Require-CommittedSource([string]$RelativePath) {
    $expectedBlob = Git-Value @('rev-parse', '--verify', "$($lock.TokCommit):$RelativePath")
    $actualBlob = Git-Value @('hash-object', '--no-filters', '--', (Join-Path $TokRoot $RelativePath))
    if ($actualBlob -cne $expectedBlob) { Reject 'E_TOK_SOURCE_DIRTY' "Tok source bytes differ from the recorded commit: $RelativePath" }
}

# Evidence always belongs to the client build tree, never either source tree.
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Builds/Tests/N0'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith($evidenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a child of client/Builds/Tests/N0.'
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$resultPath = Join-Path $OutputDirectory 'protocol-contract-result.json'
$result = [ordered]@{
    Task = 'N0'; Success = $false; Code = 'E_UNEXPECTED'; Message = ''
    CheckedAtUtc = [DateTime]::UtcNow.ToString('O')
    TokRoot = [IO.Path]::GetFullPath($TokRoot); ProtoRoot = [IO.Path]::GetFullPath($ProtoRoot)
    TokCommit = $null; FrameVersion = $null; EnvelopeSha256 = $null; ErrorCodes = $null
}
$exitCode = 1
try {
    if (-not $Check) { Reject 'E_CHECK_REQUIRED' 'Only explicit read-only -Check mode is supported.' }
    if (-not (Test-Path -LiteralPath $ProtoRoot -PathType Container)) { Reject 'E_PROTO_ROOT_MISSING' "ProtoRoot does not exist: $ProtoRoot" }
    if (-not (Test-Path -LiteralPath $TokRoot -PathType Container)) { Reject 'E_TOK_ROOT_MISSING' "TokRoot does not exist: $TokRoot" }
    foreach ($relative in @('protocol.lock.json','client-messages.json','tok/client/v1/client.proto','README.md','game/README.md','LICENSE-Tok.txt')) {
        Require-File (Join-Path $ProtoRoot $relative)
    }
    $lock = Read-Json (Join-Path $ProtoRoot 'protocol.lock.json') 'E_LOCK_JSON'
    if ($lock -isnot [Collections.IDictionary] -or $lock['SchemaVersion'] -cne 1 -or $lock['Kind'] -cne 'TokProtocolSourceLock') {
        Reject 'E_LOCK_SCHEMA' 'Expected a v1 TokProtocolSourceLock; a protoc/runtime toolchain lock is a different input.'
    }
    foreach ($name in @('CanonicalPath','TokCommit','FrameVersion','EnvelopeSha256','FrameVersionSource','ErrorCodeSource','ErrorCodes','License')) {
        if (-not $lock.Contains($name)) { Reject 'E_LOCK_SCHEMA' "Missing source-lock property: $name" }
    }
    foreach ($name in @('FrameVersionSource','ErrorCodeSource','ErrorCodes','License')) {
        if ($lock[$name] -isnot [Collections.IDictionary]) { Reject 'E_LOCK_SCHEMA' "Expected source-lock object: $name" }
    }
    foreach ($pair in @(@('FrameVersionSource','Path'),@('FrameVersionSource','Constant'),@('FrameVersionSource','Sha256'),@('ErrorCodeSource','Path'),@('ErrorCodeSource','Enum'),@('ErrorCodeSource','Sha256'),@('License','Path'),@('License','Spdx'),@('License','Sha256'))) {
        if (-not $lock[$pair[0]].Contains($pair[1])) { Reject 'E_LOCK_SCHEMA' "Missing source-lock property: $($pair -join '.')" }
    }
    $canonicalPath = 'crates/tok-proto/proto/tok/client/v1/client.proto'
    $versionPath = 'crates/tok-gateway/src/codec/mod.rs'
    $errorPath = 'crates/tok-gateway/src/error.rs'
    if ($lock.CanonicalPath -cne $canonicalPath -or $lock.FrameVersionSource.Path -cne $versionPath -or
        $lock.FrameVersionSource.Constant -cne 'VERSION' -or $lock.ErrorCodeSource.Path -cne $errorPath -or
        $lock.ErrorCodeSource.Enum -cne 'ErrorCode' -or $lock.License.Path -cne 'LICENSE' -or $lock.License.Spdx -cne 'Apache-2.0') {
        Reject 'E_LOCK_PATH' 'Protocol provenance must use the canonical Tok paths, VERSION constant, ErrorCode enum and Apache-2.0 license.'
    }
    foreach ($relative in @($canonicalPath,$versionPath,$errorPath,'LICENSE')) { Require-File (Join-Path $TokRoot $relative) }
    $gitRoot = [IO.Path]::GetFullPath((Git-Value @('rev-parse','--show-toplevel')))
    if ($gitRoot.TrimEnd('\','/') -ine ([IO.Path]::GetFullPath($TokRoot)).TrimEnd('\','/')) { Reject 'E_TOK_GIT' 'TokRoot must be the Tok repository root.' }
    $commit = Git-Value @('rev-parse','--verify','HEAD')
    if ($lock.TokCommit -cnotmatch '^[0-9a-f]{40}$' -or $lock.TokCommit -cne $commit) { Reject 'E_TOK_COMMIT' 'Source lock does not identify the current actual Tok commit.' }
    $result.TokCommit = $commit
    $canonicalHash = File-Hash (Join-Path $TokRoot $canonicalPath)
    if ($lock.EnvelopeSha256 -cne $canonicalHash) { Reject 'E_LOCK_HASH' 'Locked envelope SHA-256 differs from the canonical source bytes.' }
    if ((File-Hash (Join-Path $ProtoRoot 'tok/client/v1/client.proto')) -cne $canonicalHash) { Reject 'E_ENVELOPE_HASH' 'Shared envelope differs from the complete canonical Tok file.' }
    Require-CommittedSource $canonicalPath
    $result.EnvelopeSha256 = $canonicalHash
    if ($lock.FrameVersion -cne 3) { Reject 'E_FRAME_VERSION' 'N0 requires Frame.version 3.' }
    if ($lock.FrameVersionSource.Sha256 -cne (File-Hash (Join-Path $TokRoot $versionPath))) { Reject 'E_VERSION_SOURCE_HASH' 'Frame version source SHA-256 mismatch.' }
    Require-CommittedSource $versionPath
    $versionText = Get-Content -Raw -LiteralPath (Join-Path $TokRoot $versionPath)
    $versions = [regex]::Matches($versionText, '(?m)^pub const VERSION: u32 = ([0-9]+);\s*$')
    if ($versions.Count -ne 1 -or [uint32]$versions[0].Groups[1].Value -ne $lock.FrameVersion) { Reject 'E_FRAME_VERSION' 'Frame version is not proven by the Tok codec VERSION constant.' }
    $result.FrameVersion = $lock.FrameVersion
    if ($lock.ErrorCodeSource.Sha256 -cne (File-Hash (Join-Path $TokRoot $errorPath))) { Reject 'E_ERROR_SOURCE_HASH' 'Gateway error code source SHA-256 mismatch.' }
    Require-CommittedSource $errorPath
    $errorText = Get-Content -Raw -LiteralPath (Join-Path $TokRoot $errorPath)
    $enum = [regex]::Match($errorText, '(?s)pub enum ErrorCode\s*\{(?<body>.*?)\}')
    $actualCodes = [ordered]@{}
    foreach ($match in [regex]::Matches($enum.Groups['body'].Value, '(?m)^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*([0-9]+),\s*$')) {
        $actualCodes[$match.Groups[1].Value] = [uint32]$match.Groups[2].Value
    }
    if ($lock.ErrorCodes -isnot [Collections.IDictionary] -or $actualCodes.Count -eq 0 -or $actualCodes.Count -ne $lock.ErrorCodes.Count) { Reject 'E_ERROR_CODES' 'Lock must record every actual gateway ErrorCode.' }
    foreach ($name in $actualCodes.Keys) {
        if (-not $lock.ErrorCodes.Contains($name) -or $lock.ErrorCodes[$name] -isnot [long] -and $lock.ErrorCodes[$name] -isnot [int] -or $lock.ErrorCodes[$name] -ne $actualCodes[$name]) { Reject 'E_ERROR_CODES' "Gateway error code mismatch: $name" }
    }
    if ($actualCodes.Ok -ne 0 -or $actualCodes.AuthenticationFailed -ne 3 -or $actualCodes.Timeout -ne 7) { Reject 'E_ERROR_CODES' 'N0 login error code contract changed.' }
    $result.ErrorCodes = $actualCodes
    if ($lock.License.Sha256 -cne (File-Hash (Join-Path $TokRoot 'LICENSE')) -or
        $lock.License.Sha256 -cne (File-Hash (Join-Path $ProtoRoot 'LICENSE-Tok.txt'))) { Reject 'E_LICENSE_HASH' 'Tok license bytes or provenance do not match.' }
    Require-CommittedSource 'LICENSE'
    $admission = Read-Json (Join-Path $ProtoRoot 'client-messages.json') 'E_ADMISSION'
    if ($admission -isnot [Collections.IDictionary] -or $admission.Count -ne 2 -or
        $admission['request'] -isnot [array] -or $admission['notify'] -isnot [array] -or
        $admission.request.Count -ne 0 -or $admission.notify.Count -ne 0) { Reject 'E_ADMISSION' 'N0 requires explicit empty request and notify arrays.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $ProtoRoot 'game') -Recurse -File -Filter '*.proto').Count -ne 0) { Reject 'E_GAME_SOURCE' 'N0 has no production game .proto; test schemas belong under tests/fixtures.' }
    $result.Success = $true
    $result.Code = 'PASS'
    $result.Message = 'Complete shared envelope, committed Tok provenance, Frame v3, error codes, license and empty admission verified.'
    $exitCode = 0
} catch {
    $result.Code = if ($_.Exception.Data.Contains('ContractCode')) { $_.Exception.Data['ContractCode'] } else { 'E_UNEXPECTED' }
    $result.Message = $_.Exception.Message
} finally {
    $json = ($result | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText($resultPath, $json, [Text.UTF8Encoding]::new($false))
}
Write-Output "PROTOCOL_CONTRACT_RESULT $resultPath"
if ($result.Success) { Write-Output 'TASK_PASS N0' }
else { Write-Output "$($result.Code): $($result.Message)" }
exit $exitCode
