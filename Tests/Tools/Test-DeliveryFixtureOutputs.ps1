#requires -Version 7.0
[CmdletBinding()]
param([string]$CommRoot='E:/sgame/comm',[string]$Protoc='E:/sgame/client/Builds/tools/protoc/36.2/bin/protoc.exe')
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'Tool/Client/Unity-Common.ps1')
$id=[Guid]::NewGuid().ToString('N')
$run=Join-Path $project ('Builds/Tests/D1/FixtureOutput-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
$external=[IO.Path]::GetFullPath('E:/sgame/Builds/D1FixtureOutput/'+$id)
$sourceOwned=Join-Path $project ('Assets/Tests/Generated/D1Output-'+$id)
New-Item -ItemType Directory -Path $run,$external|Out-Null
$duplicate=Join-Path $run 'duplicate';New-Item -ItemType Directory -Path $duplicate|Out-Null
[IO.File]::WriteAllText((Join-Path $duplicate 'sentinel.txt'),'owned prior evidence')
$archive=Join-Path $run 'archive';New-Item -ItemType Directory -Path $archive|Out-Null
[IO.File]::WriteAllText((Join-Path $archive 'archive.json'),'owned immutable fixture marker')
$target=Join-Path $external 'alias-target';New-Item -ItemType Directory -Path $target|Out-Null
$alias=Join-Path $run 'alias';New-Item -ItemType Junction -Path $alias -Target $target|Out-Null
$result=[ordered]@{passed=$false;externalRoot=$external;cases=@();aliasRemoved=$false;sourceFixtureRemoved=$false}
try {
    $cases=@(
        @{name='external';output=(Join-Path $external 'legal');success=$true},
        @{name='duplicate';output=$duplicate;success=$false},
        @{name='source';output=(Join-Path $sourceOwned 'forbidden');success=$false},
        @{name='archive';output=(Join-Path $archive 'forbidden');success=$false},
        @{name='reparse';output=(Join-Path $alias 'forbidden');success=$false}
    )
    foreach($case in $cases){
        $before=@(if(Test-Path -LiteralPath $case.output){Get-ChildItem -LiteralPath $case.output -Recurse -File|ForEach-Object{$_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash}})
        $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=(Get-Command pwsh).Source;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($argument in @('-NoProfile','-File',(Join-Path $project 'Tool/Client/Verify-DeliveryFixtures.ps1'),'-CommRoot',$CommRoot,'-Protoc',$Protoc,'-OutputDirectory',$case.output)){$start.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $run ($case.name+'.log')),$stdout.Result+$stderr.Result)
        $after=@(if(Test-Path -LiteralPath $case.output){Get-ChildItem -LiteralPath $case.output -Recurse -File|ForEach-Object{$_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash}})
        $unchanged=($before.Count-eq$after.Count-and(($before|ConvertTo-Json -Compress)-ceq($after|ConvertTo-Json -Compress)))
        $passed=if($case.success){$process.ExitCode-eq0-and(Test-Path -LiteralPath (Join-Path $case.output 'result.json'))}else{$process.ExitCode-ne0-and$unchanged}
        $result.cases+=@{name=$case.name;output=$case.output;pid=$process.Id;exitCode=$process.ExitCode;processExited=$process.HasExited;expectedSuccess=$case.success;unchanged=$unchanged;passed=$passed}
    }
    $result.passed=@($result.cases|Where-Object {-not$_.passed}).Count-eq0
}catch{$result.error=$_.Exception.ToString()}
finally {
    if(Test-Path -LiteralPath $alias){if([IO.Path]::GetFullPath((Get-Item -LiteralPath $alias -Force).Target)-cne$target){throw 'Unexpected owned junction target'};[IO.Directory]::Delete($alias,$false)}
    $result.aliasRemoved=-not(Test-Path -LiteralPath $alias)
    if(Test-Path -LiteralPath $sourceOwned){$full=[IO.Path]::GetFullPath($sourceOwned);if(-not$full.StartsWith((Join-Path $project 'Assets/Tests/Generated/D1Output-'),[StringComparison]::OrdinalIgnoreCase)-or((Get-Item -LiteralPath $full -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'Unsafe owned source fixture cleanup'};Remove-Item -LiteralPath $full -Recurse;Remove-Item -LiteralPath ($full+'.meta') -ErrorAction SilentlyContinue}
    $result.sourceFixtureRemoved=-not(Test-Path -LiteralPath $sourceOwned)
    Write-RunJson (Join-Path $run 'result.json') $result
    Write-Host "DELIVERY_OUTPUT_EVIDENCE $run"
}
if(-not$result.passed-or-not$result.aliasRemoved-or-not$result.sourceFixtureRemoved){exit 1}
