#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
$adapter=Join-Path $project 'Tool/Client/Framework-NativeAdapter.ps1';if(Test-Path $adapter){. $adapter}
$root=Join-Path $project ('Builds/Tests/D2/NativeAdapter-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_ADAPTER_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_ADAPTER_FAIL $Name $($_.Exception.Message)"}}
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Owner {return @{processId=123;startedUtc='2026-10-30T00:00:00Z';executable='C:/native/Client.exe';executableSha256=('a'*64);runId='r';caseId='kill';playerBuildId='player'}}
Check 'owned PID requires exact start time and executable identity' {Assert-FrameworkOwnedProcess (Owner) (Owner)}
foreach($field in @('processId','startedUtc','executable','executableSha256','runId','caseId','playerBuildId')){
    Check "reject recycled or foreign process $field" {$expected=Owner;$observed=Owner;$observed[$field]=if($field -eq 'processId'){999}else{'different'};$rejected=$false;try{Assert-FrameworkOwnedProcess $expected $observed}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Foreign/recycled process was accepted.'}
}
Check 'all native effect entry points reject before observing OS or launching tools' {
    foreach($operation in @(
        {Start-FrameworkWindowsAttempt -Command @{file='missing.exe'} -Context @{} -OutputDirectory (Join-Path $root 'must-not-exist')},
        {Stop-FrameworkWindowsAttempt -Owned @{} -Checkpoint @{}},
        {Wait-FrameworkWindowsAttempt -Owned @{}},
        {Wait-FrameworkNativeCheckpoint -Owned @{} -NativeCaseDirectory 'missing'},
        {Start-FrameworkAndroidAttempt -Tool @{} -Command @{} -Context @{} -OutputDirectory (Join-Path $root 'must-not-exist')},
        {Get-FrameworkAndroidObservation -Tool @{} -DeviceId emulator-5554 -ApplicationId com.sgame.client -OutputDirectory (Join-Path $root 'must-not-exist')},
        {Stop-FrameworkAndroidAttempt -Tool @{} -Owned @{} -Checkpoint @{} -OutputDirectory (Join-Path $root 'must-not-exist') -Context @{}},
        {Invoke-FrameworkAdb -Tool @{file='missing-adb.exe'} -DeviceId emulator-5554 -Arguments @('get-state') -OutputFile (Join-Path $root 'must-not-exist.txt')},
        {Invoke-FrameworkReceiptCommand -Command @{file='missing.exe'} -OutputDirectory (Join-Path $root 'must-not-exist')}
        {Start-FrameworkReleaseServer -Command @{} -Context @{} -OutputDirectory (Join-Path $root 'must-not-exist')},
        {Select-FrameworkReleaseInput -Server @{} -Selection @{}},
        {Stop-FrameworkReleaseServer -Server @{}}
        {Get-FrameworkInstalledAndroidPackage -Tool @{} -Context @{} -OutputDirectory (Join-Path $root 'must-not-exist')},
        {Install-FrameworkAndroidPackage -Tool @{} -Context @{} -ApkPath 'missing.apk' -OutputDirectory (Join-Path $root 'must-not-exist')}
        {Set-FrameworkTransferPolicy -Server @{} -Selection @{} -Policy @{}},
        {Complete-FrameworkHeldTransfer -Server @{} -Holding @{} -NativeOutcome @{} -Reason owned-kill}
        {Clear-FrameworkTransferPolicy -Server @{}}
        {Prepare-FrameworkWindowsBaseline -Context @{} -OutputDirectory (Join-Path $root 'must-not-exist')}
    )){$rejected=$false;try{&$operation|Out-Null}catch [IO.InvalidDataException]{$rejected=$_.Exception.Message -like 'E_PREPARATION_GATE*'};Require $rejected 'A native effect was not blocked at the preparation gate.'}
    Require (-not (Test-Path (Join-Path $root 'must-not-exist')) -and -not (Test-Path (Join-Path $root 'must-not-exist.txt'))) 'Blocked native effects wrote output.'
}
Check 'Android process stat parsing binds actual PID and kernel start ticks' {
    $tail=@('S')+@(1..19|ForEach-Object {'0'});$tail[19]='987654321'
    $stat=ConvertFrom-FrameworkProcStat ('123 (com.sgame.client worker) '+($tail -join ' '))
    Require ($stat.processId -eq 123 -and $stat.startTicks -ceq '987654321') 'Android process start identity was parsed incorrectly.'
    $expected=@{deviceId='emulator-5554';processId=123;startTicks='987654321';bootId='11111111-1111-1111-1111-111111111111';applicationId='com.sgame.client'}
    Assert-FrameworkAndroidProcess $expected $expected
    foreach($field in @('deviceId','processId','startTicks','bootId','applicationId')){$other=$expected.Clone();$other[$field]=if($field -eq 'processId'){456}else{'different'};$rejected=$false;try{Assert-FrameworkAndroidProcess $expected $other}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Foreign/recycled Android process was accepted.'}
}
Check 'release selection checks exact published bytes without changing native state' {
    $catalog=Join-Path $root 'catalog';$release=Join-Path $catalog 'fault/release';New-Item -ItemType Directory -Path $release -Force|Out-Null
    $files=@();foreach($name in @('channel.json','channel.sig','release.json','release.sig')){$file=Join-Path $release $name;[IO.File]::WriteAllText($file,'synthetic-'+$name);$files+=@{path=('fault/release/'+$name);sha256=(Get-FrameworkFileHash $file)}}
    $receipt=@{runId='r';caseId='bad-hash';relativeDirectory='fault/release';files=$files}
    $selection=Get-FrameworkReleaseSelection $catalog $receipt 'r' 'bad-hash'
    Require ($selection.relativeDirectory -ceq 'fault/release' -and $selection.files.Count -eq 4) 'Exact selected release receipt was lost.'
    [IO.File]::AppendAllText((Join-Path $release 'channel.json'),'changed')
    $rejected=$false;try{Get-FrameworkReleaseSelection $catalog $receipt 'r' 'bad-hash'|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Changed published fault input was selected.'
}
Check 'initial Windows cleanup path is exact and cannot target a parent or another Player' {
    $data=Join-Path $root 'synthetic-data';$id='a'*32;$target=Join-Path $data ('Releases/'+$id)
    Assert-FrameworkCleanInstallPath $target $data $id (Join-Path $root 'backup')
    foreach($wrong in @($data,(Join-Path $data 'Releases'),(Join-Path $data ('Releases/'+('b'*32))))){$rejected=$false;try{Assert-FrameworkCleanInstallPath $wrong $data $id (Join-Path $root 'backup')|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Initial cleanup accepted another root/Player.'}
    $rejected=$false;try{Assert-FrameworkCleanInstallPath $target $data $id (Join-Path $target 'backup')|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Recursive backup would enter its own source tree.'
}
$failed=@($records|Where-Object passed -eq $false)
Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);cases=$records}
Write-Host "D2_ADAPTER_RECEIPT $root"
if($failed.Count){throw "D2_ADAPTER_FAILED $($failed.Count)/$($records.Count)"}
