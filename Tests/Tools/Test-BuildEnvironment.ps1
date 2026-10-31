#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$project/Tool/Client/Unity-Common.ps1"
$variables=@('SGAME_COMM_ROOT','SGAME_HYBRIDCLR_SOURCE','ANDROID_SDK_ROOT','ANDROID_NDK_ROOT','JAVA_HOME','SDK_TEST_BASE_URL')
$saved=@{};foreach($name in $variables){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
$records=[Collections.Generic.List[object]]::new()
$output=Join-Path $project ('Builds/Tests/D1/BuildEnvironment-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output|Out-Null
function Reset-Environment {foreach($name in $variables){[Environment]::SetEnvironmentVariable($name,$null)}}
function Require($Value,[string]$Message){if(-not$Value){throw $Message}}
function Check([string]$Name,[scriptblock]$Body){
 Reset-Environment
 try{&$Body;$records.Add(@{name=$Name;passed=$true});Write-Host "BUILD_ENV_PASS $Name"}
 catch{$records.Add(@{name=$Name;passed=$false;error=$_.Exception.Message});Write-Host "BUILD_ENV_FAIL $Name $($_.Exception.Message)"}
}
try{
 Check 'caller SDK NDK JDK and shared roots are preserved' {
  $env:SGAME_COMM_ROOT='D:\shared\comm'
  $env:SGAME_HYBRIDCLR_SOURCE='D:\shared\hybridclr'
  $env:ANDROID_SDK_ROOT='D:\External SDK'
  $env:ANDROID_NDK_ROOT='D:\External NDK'
  $env:JAVA_HOME='D:\External JDK'
  Set-ClientBuildEnvironment -ProjectRoot 'C:\fixture\client' -UnityEditor 'C:\fixture\Unity\Editor\Unity.exe'
  Require ($env:SGAME_COMM_ROOT -ceq 'D:\shared\comm') 'Shared protocol root was replaced.'
  Require ($env:SGAME_HYBRIDCLR_SOURCE -ceq 'D:\shared\hybridclr') 'Explicit HybridCLR environment was replaced.'
  Require ($env:ANDROID_SDK_ROOT -ceq 'D:\External SDK' -and $env:ANDROID_NDK_ROOT -ceq 'D:\External NDK' -and $env:JAVA_HOME -ceq 'D:\External JDK') 'Caller Android toolchain was replaced.'
 }
 Check 'missing roots derive from this checkout and selected Unity installation' {
  Set-ClientBuildEnvironment -ProjectRoot 'C:\fixture\client' -UnityEditor 'C:\fixture\Unity\Editor\Unity.exe'
  Require ($env:SGAME_COMM_ROOT -ceq 'C:\fixture\comm') 'Default comm must be adjacent to this checkout.'
  Require ($env:SGAME_HYBRIDCLR_SOURCE -ceq 'C:\fixture\client\Builds\dependencies\assembled-libil2cpp') 'Default HybridCLR must belong to this checkout.'
  Require ($env:ANDROID_SDK_ROOT -ceq 'C:\fixture\Unity\Editor\Data\PlaybackEngines\AndroidPlayer\SDK') 'SDK default belongs to another installation.'
  Require ($env:ANDROID_NDK_ROOT -ceq 'C:\fixture\Unity\Editor\Data\PlaybackEngines\AndroidPlayer\NDK') 'NDK default belongs to another installation.'
  Require ($env:JAVA_HOME -ceq 'C:\fixture\Unity\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK') 'JDK default belongs to another installation.'
 }
 Check 'explicit relative HybridCLR argument wins over inherited environment' {
  $env:SGAME_HYBRIDCLR_SOURCE='D:\old\hybridclr'
  Set-ClientBuildEnvironment -ProjectRoot 'C:\fixture\client' -UnityEditor 'C:\fixture\Unity\Editor\Unity.exe' -HybridClrSource 'local/assembled'
  Require ($env:SGAME_HYBRIDCLR_SOURCE -ceq 'C:\fixture\client\local\assembled') 'Explicit source was not resolved against this project.'
 }
 Check 'unset SDK repository override remains unset' {
  Set-ClientBuildEnvironment -ProjectRoot 'C:\fixture\client' -UnityEditor 'C:\fixture\Unity\Editor\Unity.exe'
  Require ([string]::IsNullOrEmpty($env:SDK_TEST_BASE_URL)) 'Build silently selected another project metadata cache.'
 }
 Check 'caller SDK repository override is preserved byte for byte' {
  $env:SDK_TEST_BASE_URL='file:///D:/verified-sdk-metadata/'
  Set-ClientBuildEnvironment -ProjectRoot 'C:\fixture\client' -UnityEditor 'C:\fixture\Unity\Editor\Unity.exe'
  Require ($env:SDK_TEST_BASE_URL -ceq 'file:///D:/verified-sdk-metadata/') 'Explicit SDK repository source was replaced.'
 }
}
finally{foreach($name in $variables){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
$failures=@($records|Where-Object passed -eq $false)
Write-RunJson (Join-Path $output 'test-result.json') @{schemaVersion=1;evidenceKind='ToolUnit';nativeAcceptance=$false;passed=($failures.Count -eq 0);cases=$records.ToArray()}
Write-Host "BUILD_ENV_RECEIPT $output"
if($failures.Count){throw "BUILD_ENV_FAILED $($failures.Count)/$($records.Count)"}
Write-Host "BUILD_ENV_SUITE_PASS $($records.Count)"
