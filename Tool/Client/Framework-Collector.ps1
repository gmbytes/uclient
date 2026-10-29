#requires -Version 7.0
# Local collection is executable. Native orchestration remains explicitly disabled in preparation.
function Get-FrameworkSlotSnapshot([string]$CapturedSlot,$Selection) {
    # This observes authenticated files selected by the native loader; it makes no independent signature/GF decision.
    Assert-FrameworkNoReparse $CapturedSlot
    $manifestPath=Join-Path $CapturedSlot 'release.manifest.json';$signaturePath=Join-Path $CapturedSlot 'release.manifest.sig'
    Require-FrameworkFact ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and (Test-Path -LiteralPath $signaturePath -PathType Leaf)) 'E_SLOT_FILES' 'Original slot manifest/signature are required.'
    Require-FrameworkFact ((Get-FrameworkFileHash $manifestPath) -ceq $Selection.ManifestSha256 -and (Get-Item -LiteralPath $signaturePath).Length -eq 256) 'E_SLOT_SELECTION' 'Captured manifest/signature differs from the actual native selection.'
    $manifest=Read-FrameworkJson $manifestPath
    Require-FrameworkFact ($manifest.releaseId -ceq $Selection.ReleaseId -and $manifest.files -is [System.Collections.IList] -and $manifest.files.Count -gt 0) 'E_SLOT_MANIFEST' 'Complete selected manifest inventory is required.'
    $files=@{};$metadata=@{}
    foreach($entry in $manifest.files){
        Require-FrameworkFact ($entry.path -is [string] -and $entry.path -cnotmatch '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))' -and -not $files.ContainsKey($entry.path)) 'E_SLOT_PATH' 'Unsafe or duplicate manifest path.'
        $path=Join-Path $CapturedSlot $entry.path
        Require-FrameworkFact ((Test-Path -LiteralPath $path -PathType Leaf) -and (Test-FrameworkInteger $entry.length) -and (Get-Item -LiteralPath $path).Length -eq $entry.length) 'E_SLOT_FILES' "Missing exact original file/length: $($entry.path)"
        $null=Resolve-FrameworkReference $CapturedSlot @{path=$entry.path;sha256=$entry.sha256}
        $files[$entry.path]=$entry.sha256
        if($entry.path.StartsWith('Metadata/',[StringComparison]::Ordinal)){$name=$entry.path.Substring(9);Require-FrameworkFact ($name -cnotmatch '[/\\]' -and $name.EndsWith('.dll',[StringComparison]::Ordinal)) 'E_METADATA' 'Original metadata must retain its exact flat .dll name.';$metadata[$name]=$entry.sha256}
    }
    $files['release.manifest.json']=Get-FrameworkFileHash $manifestPath;$files['release.manifest.sig']=Get-FrameworkFileHash $signaturePath
    Require-FrameworkFact ($files.ContainsKey('Hotfix/Client.Game.dll') -and $files.ContainsKey('Resources/GameFrameworkVersion.dat') -and $metadata.Count -gt 0) 'E_SLOT_FILES' 'Selected code/table/metadata inventory is incomplete.'
    return @{releaseId=$Selection.ReleaseId;root=$Selection.Root;capturedRoot=[IO.Path]::GetFullPath($CapturedSlot);manifestSha256=$Selection.ManifestSha256;dllSha256=$files['Hotfix/Client.Game.dll'];tableSha256=$files['Resources/GameFrameworkVersion.dat'];metadata=$metadata;files=$files}
}
function Save-FrameworkAttemptEvidence([string]$NativeAttemptDirectory,$OwnedProcess,$Context,[string]$OutputDirectory) {
    Assert-FrameworkNoReparse $NativeAttemptDirectory
    $source=[IO.Path]::GetFullPath($NativeAttemptDirectory)
    $eventPath=Join-Path $source 'events.jsonl';$events=Read-FrameworkEvents $eventPath
    $attempt=@{caseId=$OwnedProcess.caseId;attemptId=$events[0].attemptId;processId=$events[0].processId;observedProcessId=$OwnedProcess.processId;process=$OwnedProcess;events=$events}
    Require-FrameworkFact ([IO.Path]::GetFileName($source.TrimEnd('\','/')) -ceq $attempt.attemptId) 'E_ATTEMPT_DIRECTORY' 'Source directory must retain its native-generated attempt name.'
    Assert-FrameworkAttempt $attempt $Context $OwnedProcess.caseId
    $files=@();foreach($name in @('events.jsonl','checkpoint.json','complete.json','screen.png','continue')){
        $path=Join-Path $source $name
        if(Test-Path -LiteralPath $path -PathType Leaf){Assert-FrameworkNoReparse $path;$files+=@{name=$name;path=$path;sha256=(Get-FrameworkFileHash $path)}}
    }
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $descriptor=@{attemptId=$attempt.attemptId;processId=$attempt.processId;sourceDirectory=$source;evidenceKind=$Context.evidenceKind}
    foreach($file in $files){
        $destination=Join-Path $output $file.name
        $input=[IO.File]::OpenRead($file.path);$copy=[IO.FileStream]::new($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try{$input.CopyTo($copy);$copy.Flush($true)}finally{$copy.Dispose();$input.Dispose()}
        Require-FrameworkFact ((Get-FrameworkFileHash $destination) -ceq $file.sha256 -and (Get-FrameworkFileHash $file.path) -ceq $file.sha256) 'E_CAPTURE_RACE' 'Native evidence changed during collection; retain this failed capture and retry in a new directory.'
        $key=switch($file.name){'events.jsonl' {'events'};'checkpoint.json' {'checkpoint'};'complete.json' {'complete'};'screen.png' {'screen'};default {'continue'}}
        $descriptor[$key]=@{path=$file.name;sha256=$file.sha256}
    }
    Write-FrameworkJsonNew (Join-Path $output 'owned-process.json') $OwnedProcess
    $descriptor.process=@{path='owned-process.json';sha256=(Get-FrameworkFileHash (Join-Path $output 'owned-process.json'))}
    Write-FrameworkJsonNew (Join-Path $output 'collector-receipt.json') $descriptor
    return $descriptor
}
function New-FrameworkCollectionPlan {
    param([ValidateSet('Windows64','Android')][string]$Platform,[string]$RunId,[string]$PlayerPath,[string]$NativeEvidenceRoot,[string]$DeviceId,[string]$ApplicationId)
    $matrix=Get-FrameworkMatrix
    $plan=[ordered]@{schemaVersion=1;task='D2';runId=$RunId;platform=$Platform;deviceId=$DeviceId;status='NotRun';nativeExecutionEnabled=$false;cases=@();requirements=@('Explicit final PlayerArchive and successful BuildResult/receipt','Independent D1 archive approval','Explicit controller execution release','Task-owned installation/package identity','Task-owned update/Tok service and fault payload receipts')}
    foreach($definition in $matrix.cases|Where-Object {$Platform -cin $_.platforms}){
        $options=@{runId=$RunId;caseId=$definition.id;autoExit=($definition.kind -notin @('ui','session','strong-kill'))}
        if($definition.kind -ceq 'strong-kill'){$options.pausePoint=$definition.point;if($definition.statePurpose){$options.statePurpose=$definition.statePurpose}}
        if($definition.id -ceq 'disk-budget'){$options.lowSpace=$true}
        if($definition.id -ceq 'download-cancel'){$options.cancelOnTransfer=$true}
        if($definition.kind -ceq 'session' -or $definition.action -ceq 'inflight-rotation'){$options.tok=$true}
        if($definition.id -ceq 'tok-rejection'){$options.invalidCredential=$true}
        $command=New-FrameworkLaunchCommand -Platform $Platform -PlayerPath $PlayerPath -EvidenceRoot $NativeEvidenceRoot -Options $options -DeviceId $DeviceId -ApplicationId $ApplicationId
        $case=@{id=$definition.id;taskId=$definition.taskId;kind=$definition.kind;status='NotRun';attempts=@(@{role='primary';command=$command});operations=@('Verify immutable native package','Capture original state and complete K/P inventory','Select explicit task-owned signed delivery/fault input','Start owned native attempt','Bind observed OS PID and native-generated attempt','Collect original native event bytes and actual slot files','Evaluate required case')}
        if($definition.kind -in @('strong-kill','semantic-reject','throw') -or $definition.id -ceq 'download-cancel'){
            $recovery=@{runId=$RunId;caseId=$definition.id;autoExit=$true}
            $case.attempts+=@{role='recovery';command=(New-FrameworkLaunchCommand -Platform $Platform -PlayerPath $PlayerPath -EvidenceRoot $NativeEvidenceRoot -Options $recovery -DeviceId $DeviceId -ApplicationId $ApplicationId)}
            $case.operations+=@('Preserve first attempt bytes','Start complete new native PID with the same logical case ID','Prove actual K/complete-version resource, code, metadata and UI readiness')
        }
        if($definition.kind -ceq 'strong-kill'){$case.operations+=@('Wait for exact native durable checkpoint','Verify actual incomplete authenticated transfer when applicable','Terminate only the owned OS PID at that boundary','Record terminal process identity before recovery')}
        if($definition.kind -ceq 'ui'){$case.operatorAction=$definition.action;$case.operations+=@('Record operator/time/environment and screenshot hashes','Bind first/last native event sequence around actual UI input')}
        $plan.cases+=$case
    }
    return $plan
}
function Invoke-FrameworkCollection {
    param($Plan,[switch]$Execute,[scriptblock]$UnitAdapter)
    Require-FrameworkFact ($Plan.schemaVersion -eq 1) 'E_PLAN' 'Unsupported collection plan.'
    if($Execute){throw [IO.InvalidDataException]::new('E_PREPARATION_GATE: Native tool/device/server/kill execution is not released or qualified in this preparation checkpoint.')}
    if($UnitAdapter){
        # The same ordered protocol can be exercised without native effects. Its output never counts as an executed native case.
        $result=@{schemaVersion=1;evidenceKind='SyntheticUnit';nativeAcceptance=$false;cases=@()}
        foreach($case in $Plan.cases){
            $record=@{id=$case.id;status='NotRun';unitOrchestrationExecuted=$true;attempts=@()}
            $null=&$UnitAdapter 'Prepare' $case $case.attempts[0] $null
            foreach($attempt in $case.attempts){
                $owned=&$UnitAdapter 'Start' $case $attempt $null
                Require-FrameworkFact ((Test-FrameworkInteger $owned.processId) -and $owned.processId -gt 0 -and $owned.attemptId -cmatch '^[a-f0-9]{32}$' -and $owned.runId -ceq $Plan.runId -and $owned.caseId -ceq $case.id) 'E_COLLECTOR_OWNER' 'Started attempt identity is missing or foreign.'
                foreach($prior in $record.attempts){Require-FrameworkFact ($prior.processId -ne $owned.processId -and $prior.attemptId -cne $owned.attemptId) 'E_COLLECTOR_RESTART' 'Recovery did not start a new process/attempt.'}
                if($case.kind -ceq 'strong-kill' -and $attempt.role -ceq 'primary'){
                    $checkpoint=&$UnitAdapter 'WaitCheckpoint' $case $attempt $owned
                    $options=ConvertFrom-FrameworkJson $attempt.command.acceptanceJson -ObjectOnly
                    Require-FrameworkFact ($checkpoint.runId -ceq $Plan.runId -and $checkpoint.caseId -ceq $case.id -and $checkpoint.attemptId -ceq $owned.attemptId -and $checkpoint.processId -eq $owned.processId -and $checkpoint.point -ceq $options.pausePoint -and $checkpoint.purpose -ceq $options.statePurpose) 'E_COLLECTOR_CHECKPOINT' 'Checkpoint does not belong to the owned requested boundary.'
                    if($checkpoint.point -ceq 'download.partial'){Require-FrameworkFact ($checkpoint.data.bytes -gt 0 -and $checkpoint.data.bytes -lt $checkpoint.data.expectedBytes) 'E_PARTIAL_TRANSFER' 'Transfer is not an actual positive incomplete authenticated download.'}
                    $null=&$UnitAdapter 'KillOwned' $case $attempt $owned
                }elseif($case.kind -ceq 'ui'){$null=&$UnitAdapter 'ExerciseUi' $case $attempt $owned}
                elseif($case.kind -ceq 'session'){$null=&$UnitAdapter 'ExerciseSession' $case $attempt $owned}
                $terminal=&$UnitAdapter 'WaitTerminal' $case $attempt $owned
                Require-FrameworkFact ($terminal.terminal -is [bool] -and $terminal.terminal -eq $true -and $terminal.processId -eq $owned.processId) 'E_COLLECTOR_TERMINAL' 'Owned process is not terminal.'
                $capture=&$UnitAdapter 'Collect' $case $attempt $owned
                Require-FrameworkFact ($capture.evidenceKind -ceq 'SyntheticUnit') 'E_COLLECTOR_SCOPE' 'Unit orchestration cannot label native evidence.'
                $record.attempts+=@{role=$attempt.role;processId=$owned.processId;attemptId=$owned.attemptId;capture=$capture}
            }
            $result.cases+=$record
        }
        return $result
    }
    return $Plan
}
