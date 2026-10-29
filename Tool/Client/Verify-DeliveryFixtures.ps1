[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CommRoot,
    [Parameter(Mandatory)][string]$Protoc,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Unity-Common.ps1"
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture=Join-Path $project 'Tests/Fixtures/Delivery'
$CommRoot=[IO.Path]::GetFullPath($CommRoot)
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not$OutputDirectory.StartsWith((Join-Path $project 'Builds')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must remain under the owned client Builds directory.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$result=[ordered]@{passed=$false;code='E_UNEXPECTED';sources=@();generated=@();commands=@();commRoot=$CommRoot}
function Fail([string]$Code,[string]$Message){$error=[IO.InvalidDataException]::new($Message);$error.Data['Code']=$Code;throw $error}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
try {
    $manifest=Get-Content -LiteralPath (Join-Path $fixture 'provenance.json') -Raw|ConvertFrom-Json
    if($manifest.sharedSourceCommit-cnotmatch'^[a-f0-9]{40}$'-or$manifest.sources.Count-ne2){Fail 'E_PROVENANCE' 'Shared fixture source provenance is incomplete.'}
    foreach($source in $manifest.sources){
        if($source.path-notin@('proto/tests/fixtures/delivery/baseline.proto','proto/tests/fixtures/delivery/added.proto')){Fail 'E_PROVENANCE' 'Unexpected shared fixture source path.'}
        $path=[IO.Path]::GetFullPath((Join-Path $CommRoot $source.path))
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){Fail 'E_SOURCE_MISSING' "Required shared fixture source is missing: $($source.path)"}
        $hash=Hash $path
        if($hash-cne$source.sha256){Fail 'E_SOURCE_CHANGED' "Shared fixture source differs from its recorded bytes: $($source.path)"}
        $expected=@(& git -C $CommRoot rev-parse ($manifest.sharedSourceCommit+':'+$source.path));if($LASTEXITCODE-ne0-or$expected.Count-ne1){Fail 'E_SOURCE_COMMIT' 'Shared source commit does not contain the fixture.'}
        $actual=@(& git -C $CommRoot hash-object --no-filters -- $path);if($LASTEXITCODE-ne0-or$actual.Count-ne1-or$actual[0]-cne$expected[0]){Fail 'E_SOURCE_COMMIT' 'Shared fixture differs from the exact committed source blob.'}
        $result.sources+=@{path=$source.path;sha256=$hash;commit=$manifest.sharedSourceCommit;blob=$actual[0]}
    }
    foreach($file in $manifest.files){if((Hash (Join-Path $fixture $file.path))-cne$file.sha256){Fail 'E_CLIENT_GENERATED_CHANGED' "Client fixture differs from its recorded generated/template bytes: $($file.path)"}}
    if(-not(Test-Path -LiteralPath $Protoc -PathType Leaf)-or(Hash $Protoc)-cne$manifest.protocSha256){Fail 'E_PROTOC' 'The pinned protoc executable is missing or differs.'}
    $version=@(& $Protoc --version);if($LASTEXITCODE-ne0-or$version[0]-cne'libprotoc 36.2'){Fail 'E_PROTOC' 'Expected locked protoc 36.2.'}
    foreach($item in @(@('V1','baseline.proto','Baseline.cs'),@('V2','added.proto','Added.cs'))){
        $generated=Join-Path $OutputDirectory ('Generated/'+$item[0]);New-Item -ItemType Directory -Path $generated|Out-Null
        $toolArguments=@(('--proto_path='+(Join-Path $CommRoot 'proto/tests/fixtures/delivery')),('--csharp_out='+$generated),$item[1])
        $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Protoc;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        foreach($argument in $toolArguments){$start.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::Start($start);$process.WaitForExit();$result.commands+=@{file=$Protoc;arguments=$toolArguments;pid=$process.Id;exitCode=$process.ExitCode;processExited=$process.HasExited}
        if($process.ExitCode-ne0){Fail 'E_GENERATION' 'Locked shared fixture generation failed.'}
        $output=Join-Path $generated $item[2];$committed=Join-Path $fixture ($item[0]+'/Assets/Scripts/Game/Protocol/Delivery/'+$item[2])
        $actual=Hash $output;$expected=Hash $committed
        $result.generated+=@{version=$item[0];path=$output;sha256=$actual;expectedSha256=$expected;byteIdentical=($actual-ceq$expected)}
        if($actual-cne$expected){Fail 'E_GENERATED_DIFFERENCE' 'Regenerated C# differs from the checked-in artifact; do not normalize it.'}
    }
    $result.passed=$true;$result.code='PASS'
}catch{$result.error=$_.Exception.ToString();if($_.Exception.Data.Contains('Code')){$result.code=$_.Exception.Data['Code']}}
finally{Write-RunJson (Join-Path $OutputDirectory 'result.json') $result;Write-Host "DELIVERY_SOURCE_RESULT $OutputDirectory $($result.code)"}
if(-not$result.passed){exit 1}
