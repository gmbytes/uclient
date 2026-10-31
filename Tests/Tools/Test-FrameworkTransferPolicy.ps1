#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
$policyScript=Join-Path $project 'Tool/Client/Framework-TransferPolicy.ps1';if(Test-Path $policyScript){. $policyScript}
$root=Join-Path $project ('Builds/Tests/D2/Transfer-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$release=Join-Path $root 'release';New-Item -ItemType Directory -Path (Join-Path $release 'Hotfix') -Force|Out-Null
$file=Join-Path $release 'Hotfix/Client.Game.dll';[IO.File]::WriteAllBytes($file,[byte[]](0..9))
$manifest=@{releaseId='candidate-c';files=@(@{path='Hotfix/Client.Game.dll';length=10;sha256=(Get-FrameworkFileHash $file)})}
Write-FrameworkJsonNew (Join-Path $release 'release.json') $manifest
$policy=@{schemaVersion=1;runId='r';caseId='kill-download-partial';releaseId='candidate-c';manifestSha256=(Get-FrameworkFileHash (Join-Path $release 'release.json'));relativePath='Hotfix/Client.Game.dll';expectedBytes=10;sha256=(Get-FrameworkFileHash $file);holdBytes=4;timeoutSeconds=60;abortReason='owned-kill'}
$records=@()
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_TRANSFER_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_TRANSFER_FAIL $Name $($_.Exception.Message)"}}
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
Check 'hold policy binds exact authenticated manifest file length and hash' {Assert-FrameworkTransferPolicy $policy $release}
Check 'policy construction rejects a candidate file that native preparation would reuse' {
    $known=Join-Path $root 'known-good.json';Write-FrameworkJsonNew $known @{releaseId='v1';files=@(@{path='Hotfix/Client.Game.dll';length=10;sha256=('0'*64)})}
    $created=New-FrameworkTransferPolicy $release $known 'r' 'kill-download-partial' 'Hotfix/Client.Game.dll' 'owned-kill'
    Require ($created.holdBytes -gt 0 -and $created.holdBytes -lt 10 -and $created.expectedBytes -eq 10) 'Policy did not choose a bounded strict prefix.'
    [IO.File]::WriteAllText($known,($manifest|ConvertTo-Json -Depth 10))
    $rejected=$false;try{New-FrameworkTransferPolicy $release $known 'r' 'kill-download-partial' 'Hotfix/Client.Game.dll' 'owned-kill'|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'A no-download reuse path was accepted as a transfer test.'
}
foreach($field in @('manifestSha256','sha256','expectedBytes','holdBytes')){Check "reject invalid hold policy $field" {$p=$policy.Clone();$p[$field]=if($field -eq 'holdBytes'){10}elseif($field -eq 'expectedBytes'){11}else{'0'*64};$rejected=$false;try{Assert-FrameworkTransferPolicy $p $release}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Unauthenticated or already-complete hold policy was accepted.'}}
$state=@{runId='r';caseId='kill-download-partial';transferId=('a'*32);serverProcessId=321;bytesSent=4;expectedBytes=10;phase='Holding'}
$abort=@{runId='r';caseId='kill-download-partial';transferId=('a'*32);serverProcessId=321;nativeProcessId=123;reason='owned-kill'}
Check 'held stream never resumes sending on missing control' {Require ((Get-FrameworkTransferAction $policy $state $null 1) -ceq 'Hold') 'Uncontrolled response resumed.'}
Check 'matching owned abort closes response without sending its remainder' {Require ((Get-FrameworkTransferAction $policy $state $abort 1) -ceq 'Abort') 'Owned abort did not terminate the held response.'}
Check 'bounded timeout aborts instead of completing response' {Require ((Get-FrameworkTransferAction $policy $state $null 60) -ceq 'Timeout') 'Timeout resumed or completed a held transfer.'}
Check 'completed transfer and foreign abort cannot prove a strong boundary' {
    $complete=$state.Clone();$complete.bytesSent=10;$rejected=$false;try{Get-FrameworkTransferAction $policy $complete $abort 1|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Completed response was accepted as incomplete.'
    $foreign=$abort.Clone();$foreign.transferId='b'*32;$rejected=$false;try{Get-FrameworkTransferAction $policy $state $foreign 1|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Foreign abort control was accepted.'
}
Check 'server serves only signed inventory paths and public envelope files' {
    $path=Get-FrameworkPublicPayloadPath $release 'Hotfix/Client.Game.dll';Require ($path -ceq [IO.Path]::GetFullPath($file)) 'Signed payload path changed.'
    [IO.File]::WriteAllText((Join-Path $release 'unlisted.txt'),'non-secret synthetic sentinel')
    $rejected=$false;try{Get-FrameworkPublicPayloadPath $release 'unlisted.txt'|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Unlisted adjacent input could be served.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);cases=$records}
Write-Host "D2_TRANSFER_RECEIPT $root"
if($failed.Count){throw "D2_TRANSFER_FAILED $($failed.Count)/$($records.Count)"}
