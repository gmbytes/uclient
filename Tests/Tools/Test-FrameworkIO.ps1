#requires -Version 7.0
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'Tool/Client/Framework-Evidence.ps1')
$io=Join-Path $project 'Tool/Client/Framework-IO.ps1';if(Test-Path -LiteralPath $io){. $io}
$run=Join-Path $project ('Builds/Tests/D2/IO-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $run|Out-Null
$failed=@();$count=0
function Check([string]$Name,[scriptblock]$Action){$script:count++;try{&$Action;Write-Host "D2_IO_PASS $Name"}catch{$script:failed+=$Name+': '+$_.Exception.Message;Write-Host "D2_IO_FAIL $Name $($_.Exception.Message)"}}
function Require($Value,[string]$Message){if(-not $Value){throw $Message}}
Check 'Windows launch uses literal argument array and bounded acceptance controls' {
    $command=New-FrameworkLaunchCommand -Platform Windows64 -PlayerPath 'C:/archive/Player/Client.exe' -EvidenceRoot 'C:/n/Acceptance' -Options @{runId='r';caseId='kill';pausePoint='state.replace.before';statePurpose='candidate'}
    Require ($command.file -ceq 'C:/archive/Player/Client.exe' -and $command.arguments[0] -ceq '-clientAcceptance') 'Wrong native launch command.'
    $json=$command.arguments[1]|ConvertFrom-Json -AsHashtable;Require ($json.runId -ceq 'r' -and $json.statePurpose -ceq 'candidate') 'Acceptance JSON changed.'
}
Check 'Android command binds explicit selected device' {
    $command=New-FrameworkLaunchCommand -Platform Android -DeviceId emulator-5554 -PlayerPath 'C:/sdk/adb.exe' -ApplicationId com.sgame.client -EvidenceRoot '/data/user/0/p/files/Acceptance' -Options @{runId='r';caseId='ui'}
    Require ($command.arguments[0] -ceq '-s' -and $command.arguments[1] -ceq 'emulator-5554' -and $command.arguments -ccontains 'com.sgame.client/com.unity3d.player.UnityPlayerActivity') 'Wrong device or Activity command.'
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
Check 'CLI missing final archive emits failed NotRun matrix without launching tools' {
    $output=Join-Path $run 'missing-native'
    $cli=Join-Path $project 'Tool/Client/Verify-Framework.ps1'
    Require (Test-Path -LiteralPath $cli) 'Framework CLI is missing.'
    & (Get-Command pwsh).Source -NoProfile -File $cli -Platform Windows64 -PlayerArchive (Join-Path $run 'missing-archive') -OutputDirectory $output *> (Join-Path $run 'cli.log')
    Require ($LASTEXITCODE -ne 0) 'Missing final archive was reported as Pass.'
    $result=Read-FrameworkJson (Join-Path $output 'framework-result.json')
    Require (-not $result.passed -and @($result.cases|Where-Object status -eq NotRun).Count -eq 34 -and $result.execution.nativeProcessesStarted -eq 0) 'Missing native gates were not made explicit.'
}
if($failed.Count){throw "D2_IO_FAILED $($failed.Count)/$count`n$($failed -join "`n")"}
Write-Host "D2_IO_SUITE_PASS $count synthetic-only; evidence=$run"
