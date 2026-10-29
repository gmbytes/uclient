#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'Tool/Client/Framework-Evidence.ps1')
$io=Join-Path $project 'Tool/Client/Framework-IO.ps1';if(Test-Path -LiteralPath $io){. $io}
. (Join-Path $PSScriptRoot 'Framework-FileFixtures.ps1')
$run=Join-Path $project ('Builds/Tests/D2/IO-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $run|Out-Null
$failed=@();$count=0;$records=@()
function Check([string]$Name,[scriptblock]$Action){$script:count++;try{&$Action;$script:records+=@{name=$Name;passed=$true};Write-Host "D2_IO_PASS $Name"}catch{$script:failed+=$Name+': '+$_.Exception.Message;$script:records+=@{name=$Name;passed=$false;error=$_.Exception.Message};Write-Host "D2_IO_FAIL $Name $($_.Exception.Message)"}}
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
Check 'Windows launch uses literal argument array and bounded acceptance controls' {
    $command=New-FrameworkLaunchCommand -Platform Windows64 -PlayerPath 'C:/archive/Player/Client.exe' -EvidenceRoot 'C:/n/Acceptance' -Options @{runId='r';caseId='kill';pausePoint='state.replace.before';statePurpose='candidate'}
    Require ($command.file -ceq 'C:/archive/Player/Client.exe' -and $command.arguments[0] -ceq '-clientAcceptance') 'Wrong native launch command.'
    $json=$command.arguments[1]|ConvertFrom-Json -AsHashtable;Require ($json.runId -ceq 'r' -and $json.statePurpose -ceq 'candidate') 'Acceptance JSON changed.'
}
Check 'Android command binds explicit selected device' {
    $command=New-FrameworkLaunchCommand -Platform Android -DeviceId emulator-5554 -PlayerPath 'C:/sdk/adb.exe' -ApplicationId com.sgame.client -EvidenceRoot '/data/user/0/p/files/Acceptance' -Options @{runId='r';caseId='ui'}
    Require ($command.arguments[0] -ceq '-s' -and $command.arguments[1] -ceq 'emulator-5554' -and $command.arguments -ccontains 'com.sgame.client/com.unity3d.player.UnityPlayerActivity') 'Wrong device or Activity command.'
    Require ($command.arguments[-1] -ceq ("'"+$command.acceptanceJson+"'")) 'Remote adb shell must preserve the literal JSON quotes in the String extra.'
}
Check 'unsafe control and overbudget concrete evidence paths fail' {
    foreach($options in @(@{runId='../x';caseId='c'},@{runId='r';caseId='c';trustOverride=$true},@{runId='r';caseId='c';pausePoint='state.replace.before'})){
        $rejected=$false;try{New-FrameworkLaunchCommand Windows64 'C:/player.exe' 'C:/n' $options|Out-Null}catch{$rejected=$true};Require $rejected 'Unsafe native acceptance options were accepted.'
    }
    $rejected=$false;try{New-FrameworkLaunchCommand Windows64 'C:/player.exe' ('C:/'+('x'*200)) @{runId='r';caseId='c'}|Out-Null}catch{$rejected=$true};Require $rejected 'Overbudget actual evidence path was accepted.'
}
Check 'raw reference hash changes fail and duplicate JSON keys fail' {
    $file=Join-Path $run 'source.json';[IO.File]::WriteAllText($file,'{"passed":false}')
    $hash=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant();$value=Read-FrameworkReference $run @{path='source.json';sha256=$hash} -Json
    Require ($value.passed -eq $false) 'Raw reference content was not read.'
    [IO.File]::WriteAllText($file,'{"passed":true}');$rejected=$false;try{Read-FrameworkReference $run @{path='source.json';sha256=$hash} -Json|Out-Null}catch{$rejected=$true};Require $rejected 'Changed raw evidence was accepted.'
    [IO.File]::WriteAllText($file,'{"a":1,"a":2}');$rejected=$false;try{Read-FrameworkJson $file|Out-Null}catch{$rejected=$true};Require $rejected 'Duplicate JSON keys were accepted.'
}
Check 'every JSONL record rejects duplicate keys malformed UTF8 and invalid root' {
    $path=Join-Path $run 'events.jsonl'
    foreach($line in @('{"runId":"new","runId":"old"}', '{"runId":"r","data":{"ready":true,"Ready":false}}', '[]')){
        [IO.File]::WriteAllText($path,$line+"`n")
        $rejected=$false;try{Read-FrameworkEvents $path|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Invalid JSONL record was accepted.'
    }
    [IO.File]::WriteAllBytes($path,[byte[]]@(123,34,120,34,58,34,255,34,125,10))
    $rejected=$false;try{Read-FrameworkEvents $path|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Invalid UTF8 evidence was accepted.'
}
Check 'aggregation preserves source scope and refuses overwrite' {
    $source=Join-Path $run 'editor.json';[IO.File]::WriteAllText($source,'{"passed":true,"scope":"Editor"}')
    $directory=Join-Path $run 'aggregate';New-Item -ItemType Directory -Path $directory|Out-Null
    $receipt=Write-FrameworkToolEvidence -Directory $directory -RunId d2unit -TaskId U2 -ResultPath $source -Scope EditorGeometry
    $value=Read-FrameworkJson $receipt
    Require ($value.scope -ceq 'EditorGeometry' -and $value.nativeAcceptance -eq $false -and $value.source.sha256 -ceq (Get-FileHash $source).Hash.ToLowerInvariant()) 'Supplemental scope/hash was lost.'
    $before=(Get-FileHash $receipt).Hash;$rejected=$false;try{Write-FrameworkToolEvidence $directory d2unit U2 $source EditorGeometry|Out-Null}catch{$rejected=$true}
    Require ($rejected -and (Get-FileHash $receipt).Hash -ceq $before) 'Prior aggregate receipt was overwritten.'
}
Check 'missing explicit archive cannot select a latest build' {
    $rejected=$false;try{Read-FrameworkArchive (Join-Path $run 'missing') (Join-Path $run 'missing-result.json') Windows64|Out-Null}catch{$rejected=$true};Require $rejected 'Missing archive was accepted.'
}
Check 'complete synthetic archive receipt and bundle import by exact files' {
    $fixture=New-FrameworkFileBundle (Join-Path $run 'file-positive')
    $archive=Read-FrameworkArchive $fixture.archive $fixture.result Windows64
    $e=Import-FrameworkEvidence $fixture.bundlePath $archive Windows64
    Require ($e.evidenceKind -ceq 'SyntheticUnit' -and $e.playerBuildId -ceq 'player' -and $e.releases.Count -eq 2 -and $e.cases.Count -eq 0) 'File-bound bundle import lost identity/scope.'
}
Check 'foreign build receipt is rejected even if archive and result match' {
    $fixture=New-FrameworkFileBundle (Join-Path $run 'foreign-receipt')
    $path=Join-Path $fixture.bundleRoot 'receipt.json';$receipt=Read-FrameworkJson $path;$receipt.ResultFile='C:/unrelated/build-result.json';$receipt|ConvertTo-Json|Set-Content -LiteralPath $path -Encoding utf8NoBOM
    $fixture.bundle.buildReceipt.sha256=Get-FrameworkFileHash $path;$fixture.bundle|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $fixture.bundlePath -Encoding utf8NoBOM
    $archive=Read-FrameworkArchive $fixture.archive $fixture.result Windows64
    $rejected=$false;try{Import-FrameworkEvidence $fixture.bundlePath $archive Windows64|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Foreign producer receipt was accepted.'
}
Check 'archive unlisted extra file and original-byte drift are rejected' {
    foreach($mode in @('extra','changed')){
        $fixture=New-FrameworkFileBundle (Join-Path $run ('archive-'+$mode));$file=Join-Path $fixture.archive $(if($mode -eq 'extra'){'Metadata/extra.dll'}else{'Player/Client.exe'});[IO.File]::WriteAllText($file,'changed')
        $rejected=$false;try{Read-FrameworkArchive $fixture.archive $fixture.result Windows64|Out-Null}catch [IO.InvalidDataException]{$rejected=$true};Require $rejected 'Mutated original archive was accepted.'
    }
}
Check 'CLI missing final archive emits failed NotRun matrix without launching tools' {
    $output=Join-Path $run 'missing-native'
    $cli=Join-Path $project 'Tool/Client/Verify-Framework.ps1'
    Require (Test-Path -LiteralPath $cli) 'Framework CLI is missing.'
    & (Get-Command pwsh).Source -NoProfile -File $cli -Platform Windows64 -PlayerArchive (Join-Path $run 'missing-archive') -OutputDirectory $output *> (Join-Path $run 'cli.log')
    Require ($LASTEXITCODE -ne 0) 'Missing final archive was reported as Pass.'
    $result=Read-FrameworkJson (Join-Path $output 'framework-result.json')
    Require (-not $result.passed -and @($result.cases|Where-Object status -eq NotRun).Count -eq 34 -and $result.execution.nativeProcessesStarted -eq 0) 'Missing native gates were not made explicit.'
}
Write-FrameworkJsonNew (Join-Path $run 'test-result.json') @{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;passed=($failed.Count -eq 0);count=$count;cases=$records;sourceHashes=@{io=(Get-FrameworkFileHash $io);suite=(Get-FrameworkFileHash $PSCommandPath)}}
if($failed.Count){throw "D2_IO_FAILED $($failed.Count)/$count`n$($failed -join "`n")"}
Write-Host "D2_IO_SUITE_PASS $count synthetic-only; evidence=$run"
