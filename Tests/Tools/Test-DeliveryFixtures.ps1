[CmdletBinding()]
param([string]$CommRoot='E:/sgame/comm',[string]$Protoc='E:/sgame/client/Builds/tools/protoc/36.2/bin/protoc.exe')
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'Tool/Client/Unity-Common.ps1')
$run=Join-Path $project ('Builds/Tests/D1/SharedFixture-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $run|Out-Null
$result=[ordered]@{passed=$false;cases=@();sourceRestored=$false;readmeDeletionPreserved=$false}
$source=Join-Path $CommRoot 'proto/tests/fixtures/delivery/baseline.proto'
$backup=$null
$readme=Join-Path $CommRoot 'proto/game/v1/README.md'
$readmeBefore=Test-Path -LiteralPath $readme
try {
    $validator=Join-Path $project 'Tool/Client/Verify-DeliveryFixtures.ps1'
    if(-not(Test-Path -LiteralPath $validator)){throw 'Required shared fixture source validator is missing.'}
    $backup=[IO.File]::ReadAllBytes($source)
    foreach($case in @('valid','missing','changed')) {
        if($case-eq'missing'){Remove-Item -LiteralPath $source}
        if($case-eq'changed'){[IO.File]::WriteAllBytes($source,($backup+[Text.Encoding]::UTF8.GetBytes("`n// controlled source drift`n")))}
        $output=Join-Path $run $case
        $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=(Get-Command pwsh).Source;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($arg in @('-NoProfile','-File',$validator,'-CommRoot',$CommRoot,'-Protoc',$Protoc,'-OutputDirectory',$output)){$start.ArgumentList.Add($arg)}
        $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $run ($case+'.log')),$stdout.Result+$stderr.Result)
        $actual=Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw|ConvertFrom-Json
        $expected=if($case-eq'valid'){'PASS'}elseif($case-eq'missing'){'E_SOURCE_MISSING'}else{'E_SOURCE_CHANGED'}
        $result.cases+=@{case=$case;pid=$process.Id;exitCode=$process.ExitCode;processExited=$process.HasExited;expected=$expected;actual=$actual.code}
        [IO.File]::WriteAllBytes($source,$backup)
        if($actual.code-cne$expected -or ($case-eq'valid'-and$process.ExitCode-ne0) -or ($case-ne'valid'-and$process.ExitCode-eq0)){throw "Unexpected validation result for $case"}
    }
    $result.passed=$true
} catch {$result.error=$_.Exception.ToString()}
finally {
    if($null-ne$backup){[IO.File]::WriteAllBytes($source,$backup);$result.sourceRestored=([Convert]::ToBase64String([IO.File]::ReadAllBytes($source))-ceq[Convert]::ToBase64String($backup))}
    $result.readmeDeletionPreserved=((Test-Path -LiteralPath $readme)-eq$readmeBefore)
    Write-RunJson (Join-Path $run 'result.json') $result
    Write-Host "DELIVERY_FIXTURE_EVIDENCE $run"
}
if(-not$result.passed-or-not$result.sourceRestored-or-not$result.readmeDeletionPreserved){exit 1}
