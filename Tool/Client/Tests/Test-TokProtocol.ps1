#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$TokRoot = 'E:/sgame/tok',
    [string]$ProtoRoot = 'E:/sgame/comm/proto',
    [string]$RunId = ('suite-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N'))
)
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') { throw 'RunId must be one directory name.' }
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$run = Join-Path $clientRoot "Builds/Tests/N0/$RunId"
New-Item -ItemType Directory -Force $run | Out-Null
$checker = Join-Path $PSScriptRoot '../Check-TokProtocol.ps1'
$fixtures = Join-Path $ProtoRoot 'tests/fixtures/TokContract'
$pwsh = (Get-Process -Id $PID).Path
$failures = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()

function Snapshot([string]$Root) {
    $map = [ordered]@{}
    if (Test-Path -LiteralPath $Root -PathType Container) {
        Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName | ForEach-Object {
            $map[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    return ($map | ConvertTo-Json -Compress -Depth 4)
}

# Each case runs the actual entry point in a child process. These catch missing
# validation branches, accepted drift, false pass markers and source writes.
function Case([string]$Name, [string]$ExpectedCode, [scriptblock]$Arrange, [switch]$Shared, [string]$SourceRoot = $TokRoot) {
    $caseRoot = Join-Path $run $Name
    $inputRoot = if ($Shared) { $ProtoRoot } else { Join-Path $caseRoot 'input' }
    New-Item -ItemType Directory -Force $caseRoot | Out-Null
    if (-not $Shared) {
        New-Item -ItemType Directory -Force $inputRoot | Out-Null
        Copy-Item -Path (Join-Path $fixtures 'Valid/*') -Destination $inputRoot -Recurse
    }
    & $Arrange $inputRoot
    $before = Snapshot $inputRoot
    $outputDir = Join-Path $caseRoot 'evidence'
    $sourceBefore = @($tokPaths | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $SourceRoot $_) -Algorithm SHA256).Hash }) -join ','
    $output = @(& $pwsh -NoLogo -NoProfile -File $checker -TokRoot $SourceRoot -ProtoRoot $inputRoot -Check -OutputDirectory $outputDir 2>&1)
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $caseRoot 'process.log') -Encoding utf8
    $errorMessage = $null
    try {
        if ($ExpectedCode -eq 'PASS') {
            if ($exitCode -ne 0) { throw "Expected exit 0, got $exitCode" }
            if (@($output | Where-Object { "$_" -ceq 'TASK_PASS N0' }).Count -ne 1) { throw 'Missing unique pass marker' }
        } else {
            if ($exitCode -eq 0) { throw 'Invalid input was accepted (exit 0)' }
            if (@($output | Where-Object { "$_" -ceq 'TASK_PASS N0' }).Count -ne 0) { throw 'Failure emitted pass marker' }
        }
        $report = Get-Content -Raw -LiteralPath (Join-Path $outputDir 'protocol-contract-result.json') | ConvertFrom-Json
        if ($report.Code -cne $ExpectedCode) { throw "Expected code $ExpectedCode, got $($report.Code)" }
        if ($report.Task -cne 'N0' -or $report.Success -ne ($ExpectedCode -eq 'PASS')) { throw 'Invalid result status' }
        if ($ExpectedCode -eq 'PASS' -and ($report.FrameVersion -ne 3 -or $report.ErrorCodes.Ok -ne 0 -or $report.ErrorCodes.AuthenticationFailed -ne 3 -or $report.ErrorCodes.Timeout -ne 7)) { throw 'Missing verified contract facts' }
        if ((Snapshot $inputRoot) -cne $before) { throw 'Checker wrote protocol inputs' }
        $sourceAfter = @($tokPaths | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $SourceRoot $_) -Algorithm SHA256).Hash }) -join ','
        if ($sourceBefore -cne $sourceAfter) { throw 'Checker wrote Tok source inputs' }
    } catch { $errorMessage = $_.Exception.Message }
    $results.Add([ordered]@{ Name=$Name; Expected=$ExpectedCode; ExitCode=$exitCode; Passed=($null -eq $errorMessage); Error=$errorMessage })
    if ($errorMessage) { $failures.Add("${Name}: $errorMessage"); Write-Output "FAIL ${Name}: $errorMessage" }
    else { Write-Output "PASS $Name ($ExpectedCode; exit $exitCode)" }
}

function Edit-Lock([string]$Root, [scriptblock]$Edit) {
    $path = Join-Path $Root 'protocol.lock.json'
    $lock = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    & $Edit $lock
    $lock | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding utf8
}

function Remove-TestInput([string]$Root, [switch]$ContentsOnly) {
    $resolved = [IO.Path]::GetFullPath($Root)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($run) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to remove a path outside this test run.' }
    if ($ContentsOnly) { Get-ChildItem -LiteralPath $resolved -Force | Remove-Item -Recurse }
    else { Remove-Item -LiteralPath $resolved -Recurse }
}

$tokPaths = @('crates/tok-proto/proto/tok/client/v1/client.proto','crates/tok-gateway/src/error.rs','crates/tok-gateway/src/codec/mod.rs','LICENSE')
$tokBefore = @($tokPaths | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $TokRoot $_) -Algorithm SHA256).Hash }) -join ','
Case Valid PASS {}
Case SharedSource PASS {} -Shared
Case MissingRoot E_PROTO_ROOT_MISSING { param($root) Remove-TestInput $root }
Case EmptyRoot E_REQUIRED_PATH_MISSING { param($root) Remove-TestInput $root -ContentsOnly }
Case MissingEnvelope E_REQUIRED_PATH_MISSING { param($root) Remove-Item -LiteralPath (Join-Path $root 'tok/client/v1/client.proto') }
Case ChangedFieldNumber E_ENVELOPE_HASH { param($root) Copy-Item -LiteralPath (Join-Path $fixtures 'ChangedFieldNumber/client.proto') -Destination (Join-Path $root 'tok/client/v1/client.proto') }
Case ChangedOneof E_ENVELOPE_HASH { param($root) Copy-Item -LiteralPath (Join-Path $fixtures 'ChangedOneof/client.proto') -Destination (Join-Path $root 'tok/client/v1/client.proto') }
Case WrongLock E_LOCK_HASH { param($root) Copy-Item -LiteralPath (Join-Path $fixtures 'WrongLock/protocol.lock.json') -Destination (Join-Path $root 'protocol.lock.json') }
Case WrongCommit E_TOK_COMMIT { param($root) Edit-Lock $root { param($lock) $lock.TokCommit = '0000000000000000000000000000000000000000' } }
Case WrongFrameVersion E_FRAME_VERSION { param($root) Edit-Lock $root { param($lock) $lock.FrameVersion = 2 } }
Case WrongVersionHash E_VERSION_SOURCE_HASH { param($root) Edit-Lock $root { param($lock) $lock.FrameVersionSource.Sha256 = '0' * 64 } }
Case WrongErrorHash E_ERROR_SOURCE_HASH { param($root) Edit-Lock $root { param($lock) $lock.ErrorCodeSource.Sha256 = '0' * 64 } }
Case WrongErrorCode E_ERROR_CODES { param($root) Edit-Lock $root { param($lock) $lock.ErrorCodes.AuthenticationFailed = 401 } }
Case WrongCanonicalPath E_LOCK_PATH { param($root) Edit-Lock $root { param($lock) $lock.CanonicalPath = '../outside.proto' } }
Case ToolchainLock E_LOCK_SCHEMA { param($root) '{"protoc":{"version":"36.2"}}' | Set-Content -LiteralPath (Join-Path $root 'protocol.lock.json') }
Case MalformedLock E_LOCK_JSON { param($root) '{' | Set-Content -LiteralPath (Join-Path $root 'protocol.lock.json') }
Case MissingAdmission E_REQUIRED_PATH_MISSING { param($root) Remove-Item -LiteralPath (Join-Path $root 'client-messages.json') }
Case NonemptyAdmission E_ADMISSION { param($root) '{"request":[{"full_name":"demo.Player"}],"notify":[]}' | Set-Content -LiteralPath (Join-Path $root 'client-messages.json') }
Case MalformedAdmission E_ADMISSION { param($root) '{"request":null,"notify":[]}' | Set-Content -LiteralPath (Join-Path $root 'client-messages.json') }
Case MissingAdmissionKeys E_ADMISSION { param($root) '{"request":[],"other":[]}' | Set-Content -LiteralPath (Join-Path $root 'client-messages.json') }
Case MissingLicense E_REQUIRED_PATH_MISSING { param($root) Remove-Item -LiteralPath (Join-Path $root 'LICENSE-Tok.txt') }
Case WrongLicense E_LICENSE_HASH { param($root) 'not the Tok license' | Set-Content -LiteralPath (Join-Path $root 'LICENSE-Tok.txt') }
Case UnexpectedGameProto E_GAME_SOURCE { param($root) 'syntax = "proto3";' | Set-Content -LiteralPath (Join-Path $root 'game/accidental.proto') }

# A tiny local repository exercises git provenance without touching Tok's
# checkout or requiring clone transport. Its four source files are exact copies.
$sandboxTok = Join-Path $run 'tok-source-sandbox'
& git init --quiet $sandboxTok
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated source test repository.' }
foreach ($relative in $tokPaths) {
    $target = Join-Path $sandboxTok $relative
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $TokRoot $relative) -Destination $target
}
& git -C $sandboxTok -c core.autocrlf=false add -- @tokPaths
if ($LASTEXITCODE -ne 0) { throw 'Could not stage source test inputs.' }
& git -C $sandboxTok -c user.name=N0Fixture -c user.email=n0@invalid -c commit.gpgsign=false commit --quiet -m 'test fixture source baseline'
if ($LASTEXITCODE -ne 0) { throw 'Could not commit source test fixture baseline.' }
$sandboxCommit = (& git -C $sandboxTok rev-parse HEAD).Trim()
Case DirtyTokSource E_TOK_SOURCE_DIRTY {
    param($root)
    $canonical = Join-Path $sandboxTok 'crates/tok-proto/proto/tok/client/v1/client.proto'
    [IO.File]::AppendAllText($canonical, "`n// deliberately uncommitted source drift`n", [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $canonical -Destination (Join-Path $root 'tok/client/v1/client.proto')
    $hash = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash.ToLowerInvariant()
    Edit-Lock $root { param($lock) $lock.EnvelopeSha256 = $hash; $lock.TokCommit = $sandboxCommit }
} -SourceRoot $sandboxTok
$tokAfter = @($tokPaths | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $TokRoot $_) -Algorithm SHA256).Hash }) -join ','
if ($tokBefore -cne $tokAfter) { $failures.Add('Checker wrote Tok source files') }
[ordered]@{ Total=$results.Count; Failed=$failures.Count; Results=@($results); Failures=@($failures) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'suite-result.json') -Encoding utf8
Write-Output "Evidence: $run"
Write-Output "$($results.Count - $failures.Count)/$($results.Count) cases passed"
if ($failures.Count) { exit 1 }
exit 0
