#requires -Version 7.0
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Framework-Evidence.ps1"
. "$project/Tool/Client/Framework-IO.ps1"
. "$project/Tool/Client/Framework-NativeAdapter.ps1"
$root=Join-Path $project ('Builds/Tests/D2/InitialInstall-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$records=@()
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_INSTALL_PASS $Name"}catch{$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_INSTALL_FAIL $Name $($_.Exception.Message)"}}
function Context {return @{scope='NativeExecution';platform='Android';deviceId='emulator-5554';profilePackageId='com.sgame.client.acceptance';playerBuildId=('a'*32);archiveNative=@{'Client.apk'=('b'*64)};installationPhase='BeforeBaseline';completedRequiredCases=0;initialInstallConsumed=$false}}
function Installed {return @{deviceId='emulator-5554';applicationId='com.sgame.client.acceptance';present=$true;apkSha256=('b'*64)}}
Check 'only original task package before baseline may be replaced' {Assert-FrameworkAndroidInitialInstall (Context) (Installed)}
foreach($field in @('scope','platform','deviceId','profilePackageId','installationPhase','completedRequiredCases','initialInstallConsumed')){
    Check "invalid initial installation context rejects $field" {$c=Context;$c[$field]=if($field -ceq 'completedRequiredCases'){1}elseif($field -ceq 'initialInstallConsumed'){$true}else{'foreign'};$caught=$false;try{Assert-FrameworkAndroidInitialInstall $c (Installed)}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign/later-phase reset was accepted.'}
}
Check 'foreign installed APK rejects before removal' {$i=Installed;$i.apkSha256='c'*64;$caught=$false;try{Assert-FrameworkAndroidInitialInstall (Context) $i}catch [IO.InvalidDataException]{$caught=$true};Require $caught 'Foreign installed package was accepted.'}
Check 'genuinely absent package requires no removal' {$i=Installed;$i.present=$false;$i.Remove('apkSha256');Assert-FrameworkAndroidInitialInstall (Context) $i}
Check 'unscoped initial prepare rejects before effects' {$caught=$false;try{Prepare-FrameworkAndroidBaseline -Tool @{} -Context @{} -ApkPath 'missing' -OutputDirectory (Join-Path $root 'forbidden')}catch [IO.InvalidDataException]{$caught=$_.Exception.Message -like 'E_PREPARATION_GATE*'};Require ($caught -and -not(Test-Path (Join-Path $root 'forbidden'))) 'Unscoped prepare caused effects.'}
$failed=@($records|Where-Object passed -eq $false);Write-FrameworkJsonNew (Join-Path $root 'test-result.json') @{evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$records.Count;cases=$records};Write-Host "D2_INSTALL_RECEIPT $root";if($failed.Count){throw "D2_INSTALL_FAILED $($failed.Count)/$($records.Count)"}
