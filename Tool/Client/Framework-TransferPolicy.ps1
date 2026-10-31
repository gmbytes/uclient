#requires -Version 7.0
# Pure policy validation used by the owned D2 HTTP server and fixture tests.
function Get-FrameworkPublicPayloadPath([string]$ReleaseDirectory,[string]$RelativePath) {
    Require-FrameworkFact ($RelativePath -is [string] -and $RelativePath -cnotmatch '(^[/\\]|[\\:]|(^|/)\.\.?(/|$))') 'E_SERVER_PATH' 'Unsafe payload path.'
    $root=[IO.Path]::GetFullPath($ReleaseDirectory);$path=[IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    Require-FrameworkFact ($path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_SERVER_PATH' 'Payload escapes selected release.'
    if($RelativePath -cnotin @('channel.json','channel.sig','release.json','release.sig')){
        $manifest=Read-FrameworkJson (Join-Path $root 'release.json')
        Require-FrameworkFact (@($manifest.files|Where-Object path -CEQ $RelativePath).Count -eq 1) 'E_SERVER_INVENTORY' 'Only exact signed inventory paths and public envelopes may be served.'
    }
    Assert-FrameworkNoReparse $path
    return $path
}
function New-FrameworkTransferPolicy([string]$ReleaseDirectory,[string]$KnownGoodManifest,[string]$RunId,[string]$CaseId,[string]$RelativePath,[ValidateSet('owned-kill','cancel','network-interruption')][string]$AbortReason) {
    $manifestPath=Join-Path $ReleaseDirectory 'release.json';$manifest=Read-FrameworkJson $manifestPath;$known=Read-FrameworkJson $KnownGoodManifest
    $target=@($manifest.files|Where-Object path -CEQ $RelativePath)
    Require-FrameworkFact ($target.Count -eq 1 -and (Test-FrameworkInteger $target[0].length) -and $target[0].length -gt 1) 'E_TRANSFER_TARGET' 'One authenticated file with a positive incomplete prefix is required.'
    $prior=@($known.files|Where-Object path -CEQ $RelativePath)
    Require-FrameworkFact ($prior.Count -eq 0 -or ($prior.Count -eq 1 -and $prior[0].sha256 -cne $target[0].sha256)) 'E_TRANSFER_REUSE' 'Native preparation would reuse this known-good file; establish the required signed baseline before testing actual transfer.'
    $policy=@{schemaVersion=1;runId=$RunId;caseId=$CaseId;releaseId=$manifest.releaseId;manifestSha256=(Get-FrameworkFileHash $manifestPath);relativePath=$RelativePath;expectedBytes=$target[0].length;sha256=$target[0].sha256;holdBytes=[long][Math]::Min(4096,$target[0].length-1);timeoutSeconds=120;abortReason=$AbortReason}
    Assert-FrameworkTransferPolicy $policy $ReleaseDirectory
    return $policy
}
function Assert-FrameworkTransferPolicy($Policy,[string]$ReleaseDirectory) {
    Assert-FrameworkFields $Policy @('schemaVersion','runId','caseId','releaseId','manifestSha256','relativePath','expectedBytes','sha256','holdBytes','timeoutSeconds','abortReason')
    Require-FrameworkFact ($Policy.schemaVersion -eq 1 -and $Policy.runId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $Policy.caseId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $Policy.releaseId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $Policy.abortReason -cin @('owned-kill','cancel','network-interruption')) 'E_TRANSFER_POLICY' 'Bounded run/case/release and exact transfer purpose are required.'
    foreach($field in @('expectedBytes','holdBytes','timeoutSeconds')){Require-FrameworkFact (Test-FrameworkInteger $Policy[$field]) 'E_TRANSFER_POLICY' 'Transfer byte/deadline fields must be integers.'}
    Require-FrameworkFact ($Policy.expectedBytes -gt 1 -and $Policy.holdBytes -gt 0 -and $Policy.holdBytes -lt $Policy.expectedBytes -and $Policy.timeoutSeconds -ge 1 -and $Policy.timeoutSeconds -le 120) 'E_TRANSFER_POLICY' 'A positive strict prefix and bounded abort-only deadline are required.'
    $manifestPath=Join-Path $ReleaseDirectory 'release.json'
    Require-FrameworkFact ((Get-FrameworkFileHash $manifestPath) -ceq $Policy.manifestSha256) 'E_TRANSFER_MANIFEST' 'Hold policy must bind the exact authenticated candidate manifest bytes.'
    $manifest=Read-FrameworkJson $manifestPath
    $files=@($manifest.files|Where-Object {$_.path -ceq $Policy.relativePath -and $_.length -eq $Policy.expectedBytes -and $_.sha256 -ceq $Policy.sha256})
    Require-FrameworkFact ($manifest.releaseId -ceq $Policy.releaseId -and $files.Count -eq 1) 'E_TRANSFER_MANIFEST' 'Held path/Content-Length/hash differs from the authenticated candidate inventory.'
    $path=Resolve-FrameworkReference $ReleaseDirectory @{path=$Policy.relativePath;sha256=$Policy.sha256}
    Require-FrameworkFact ((Get-Item -LiteralPath $path).Length -eq $Policy.expectedBytes) 'E_TRANSFER_FILE' 'Held source file length changed.'
}
function Get-FrameworkTransferAction($Policy,$State,$Control,[double]$ElapsedSeconds) {
    Require-FrameworkFact ($State.phase -ceq 'Holding' -and $State.runId -ceq $Policy.runId -and $State.caseId -ceq $Policy.caseId -and $State.transferId -cmatch '^[a-f0-9]{32}$' -and (Test-FrameworkInteger $State.serverProcessId) -and $State.serverProcessId -gt 0 -and $State.bytesSent -eq $Policy.holdBytes -and $State.bytesSent -gt 0 -and $State.bytesSent -lt $State.expectedBytes -and $State.expectedBytes -eq $Policy.expectedBytes) 'E_TRANSFER_STATE' 'The response must still be held at its positive incomplete byte prefix.'
    if($null -ne $Control){
        Require-FrameworkFact ($Control.runId -ceq $State.runId -and $Control.caseId -ceq $State.caseId -and $Control.transferId -ceq $State.transferId -and $Control.serverProcessId -eq $State.serverProcessId -and (Test-FrameworkInteger $Control.nativeProcessId) -and $Control.nativeProcessId -gt 0 -and $Control.reason -ceq $Policy.abortReason) 'E_TRANSFER_CONTROL' 'Abort control belongs to another run/case/server/transfer or purpose.'
        return 'Abort'
    }
    if($ElapsedSeconds -ge $Policy.timeoutSeconds){return 'Timeout'}
    return 'Hold'
}
