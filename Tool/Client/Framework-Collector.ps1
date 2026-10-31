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
    Require-FrameworkFact ($files.ContainsKey('Hotfix/Client.Game.dll') -and $files.ContainsKey('Hotfix/Client.Game.Protocol.dll') -and $files.ContainsKey('Resources/GameFrameworkVersion.dat') -and $metadata.Count -gt 0) 'E_SLOT_FILES' 'Selected Game+Protocol DLL/table/metadata inventory is incomplete.'
    return @{releaseId=$Selection.ReleaseId;root=$Selection.Root;capturedRoot=[IO.Path]::GetFullPath($CapturedSlot);manifestSha256=$Selection.ManifestSha256;dllSha256=$files['Hotfix/Client.Game.dll'];protocolDllSha256=$files['Hotfix/Client.Game.Protocol.dll'];tableSha256=$files['Resources/GameFrameworkVersion.dat'];metadata=$metadata;files=$files}
}
function Save-FrameworkAttemptEvidence([string]$NativeAttemptDirectory,$OwnedProcess,$Context,[string]$OutputDirectory,[switch]$DiagnosticCapture) {
    Assert-FrameworkNoReparse $NativeAttemptDirectory
    $source=[IO.Path]::GetFullPath($NativeAttemptDirectory)
    $eventPath=Join-Path $source 'events.jsonl';$events=Read-FrameworkEvents $eventPath
    $attempt=@{caseId=$OwnedProcess.caseId;attemptId=$events[0].attemptId;processId=$events[0].processId;observedProcessId=$OwnedProcess.processId;process=$OwnedProcess;events=$events}
    Require-FrameworkFact ([IO.Path]::GetFileName($source.TrimEnd('\','/')) -ceq $attempt.attemptId) 'E_ATTEMPT_DIRECTORY' 'Source directory must retain its native-generated attempt name.'
    Assert-FrameworkAttemptEnvelope $attempt $Context $OwnedProcess.caseId
    $schemaAccepted=$true;$rejection=$null
    try{Assert-FrameworkAttempt $attempt $Context $OwnedProcess.caseId}catch [IO.InvalidDataException]{
        if(-not $DiagnosticCapture){throw}
        $schemaAccepted=$false;$rejection=$_.Exception.Message
    }
    $files=@();foreach($name in @('events.jsonl','checkpoint.json','complete.json','screen.png','continue')){
        $path=Join-Path $source $name
        if(Test-Path -LiteralPath $path -PathType Leaf){Assert-FrameworkNoReparse $path;$files+=@{name=$name;path=$path;sha256=(Get-FrameworkFileHash $path)}}
    }
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $descriptor=@{attemptId=$attempt.attemptId;processId=$attempt.processId;sourceDirectory=$source;evidenceKind=$Context.evidenceKind;schemaAccepted=$schemaAccepted;nativeAcceptance=$false;rejection=$rejection}
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
    $plan=[ordered]@{schemaVersion=1;task='D2';runId=$RunId;platform=$Platform;deviceId=$DeviceId;nativeEvidenceRoot=$NativeEvidenceRoot;status='NotRun';nativeExecutionEnabled=$false;cases=@();requirements=@('Explicit final PlayerArchive and successful BuildResult/receipt','Independent D1 archive approval','Explicit controller execution release','Task-owned installation/package identity','Task-owned update/Tok service and fault payload receipts')}
    foreach($definition in $matrix.cases|Where-Object {$Platform -cin $_.platforms}){
        $options=@{runId=$RunId;caseId=$definition.id;autoExit=($definition.kind -notin @('ui','session','strong-kill'))}
        if($definition.kind -ceq 'strong-kill'){$options.pausePoint=$definition.point;if($definition.statePurpose){$options.statePurpose=$definition.statePurpose}}
        if($definition.id -ceq 'disk-budget'){$options.lowSpace=$true}
        if($definition.id -ceq 'download-cancel'){$options.cancelOnTransfer=$true}
        if($definition.kind -ceq 'session' -or $definition.action -ceq 'inflight-rotation'){$options.tok=$true}
        if($definition.id -ceq 'tok-rejection'){$options.invalidCredential=$true}
        $command=New-FrameworkLaunchCommand -Platform $Platform -PlayerPath $PlayerPath -EvidenceRoot $NativeEvidenceRoot -Options $options -DeviceId $DeviceId -ApplicationId $ApplicationId
        $case=@{id=$definition.id;taskId=$definition.taskId;kind=$definition.kind;status='NotRun';attempts=@(@{role='primary';command=$command});operations=@('Verify immutable native package','Capture original state and complete K/P inventory','Select explicit task-owned signed delivery/fault input','Start owned native attempt','Bind observed OS PID and native-generated attempt','Collect original native event bytes and actual slot files','Evaluate required case')}
        if($definition.setupRelease){$case.setupRelease=$definition.setupRelease;$case.setupMethod='ordinary-signed-channel-new-process';$case.operations=@('Establish required known-good through ordinary signed channel in a new PID; capture setup separately')+$case.operations}
        if($definition.allowedRecovery){$case.allowedRecovery=$definition.allowedRecovery;$case.recoveryInput=$definition.recoveryInput;$case.forbidRecoveryPayloadDownloads=$true}
        if($definition.requiresServerHold){$case.requiresServerHold=$true;$case.operations+=@('Hold authenticated response at a positive strict prefix until owned kill/cancel/interrupt; never resume body')}
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
function Bind-FrameworkCollectionPlan($Plan,$Archive,[string]$BuildReceipt,[string]$RuntimePlayerRoot,$AdbTool=$null) {
    $receipt=Read-FrameworkJson $BuildReceipt
    Assert-FrameworkFields $receipt @('Status','OutputDirectory','ResultFile')
    Require-FrameworkFact (($receipt.Status -ceq 'Succeeded' -or ((Test-FrameworkInteger $receipt.Status) -and $receipt.Status -eq 1)) -and [IO.Path]::GetFullPath($receipt.ResultFile) -ceq $Archive.resultFile -and [IO.Path]::GetFullPath($receipt.OutputDirectory) -ceq $Archive.outputDirectory) 'E_PLAN_RECEIPT' 'Explicit actual producer receipt must own the selected original archive.'
    $expectedPlatform=if($Plan.platform -ceq 'Windows64'){'StandaloneWindows64'}else{'Android'}
    Require-FrameworkFact ($Plan.platform -cin @('Windows64','Android') -and $Archive.identity.Platform -ceq $expectedPlatform -and $Plan.runId -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_PLAN_IDENTITY' 'Plan platform/run differs from its native archive.'
    $inventory=Get-FrameworkInventory $RuntimePlayerRoot;$actual=@{};foreach($name in $inventory.Keys){$actual[$name]=$inventory[$name].sha256}
    Assert-FrameworkHashMap $Archive.archiveNative $actual 'Runnable original native package'
    $bound=ConvertFrom-FrameworkJson ($Plan|ConvertTo-Json -Depth 100 -Compress) -ObjectOnly
    if($Plan.platform -ceq 'Windows64'){$executable=[IO.Path]::GetFullPath((Join-Path $RuntimePlayerRoot 'Client.exe'));$sha=$Archive.archiveNative['Client.exe']}
    else{
        Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($Plan.deviceId) -and $AdbTool.file -is [string] -and $AdbTool.sha256 -cmatch '^[a-f0-9]{64}$' -and (Get-FrameworkFileHash $AdbTool.file) -ceq $AdbTool.sha256) 'E_ADB_RECEIPT' 'Android plan requires the explicit selected device and pinned adb tool receipt.'
        $executable=[IO.Path]::GetFullPath($AdbTool.file);$sha=$AdbTool.sha256
    }
    foreach($case in $bound.cases){foreach($attempt in $case.attempts){
        Require-FrameworkFact ([IO.Path]::GetFullPath($attempt.command.file) -ceq $executable) 'E_PLAN_EXECUTABLE' 'Plan command points outside the exact runnable package/tool.'
        $options=ConvertFrom-FrameworkJson $attempt.command.acceptanceJson -ObjectOnly
        Require-FrameworkFact ($options.runId -ceq $Plan.runId -and $options.caseId -ceq $case.id) 'E_PLAN_CASE' 'Command controls differ from their logical case.'
        $expected=New-FrameworkLaunchCommand -Platform $Plan.platform -PlayerPath $executable -EvidenceRoot $Plan.nativeEvidenceRoot -Options $options -DeviceId $Plan.deviceId -ApplicationId $Archive.profilePackageId
        if($Plan.platform -ceq 'Windows64'){$expected.arguments[1]=$attempt.command.acceptanceJson}else{$expected.arguments[-1]="'"+$attempt.command.acceptanceJson+"'"}
        Require-FrameworkFact ($attempt.command.arguments -is [System.Collections.IList] -and $attempt.command.arguments.Count -eq $expected.arguments.Count) 'E_PLAN_ARGUMENTS' 'Command includes unreviewed or missing native launch tokens.'
        for($i=0;$i -lt $expected.arguments.Count;$i++){Require-FrameworkFact ($attempt.command.arguments[$i] -is [string] -and $attempt.command.arguments[$i] -ceq $expected.arguments[$i]) 'E_PLAN_ARGUMENTS' 'Command tokens differ from the exact bounded native launch recipe.'}
        $attempt.command.sha256=$sha
    }}
    $bound.binding=@{playerBuildId=$Archive.playerBuildId;profilePackageId=$Archive.profilePackageId;archiveSha256=$Archive.archiveSha256;buildResultSha256=$Archive.buildResultSha256;buildReceipt=@{path=[IO.Path]::GetFullPath($BuildReceipt);sha256=(Get-FrameworkFileHash $BuildReceipt)};runtimePlayerRoot=[IO.Path]::GetFullPath($RuntimePlayerRoot);nativeInventory=$actual;nativeBinaryEvidence=$Archive.nativeBinaryEvidence}
    $bound.nativeExecutionEnabled=$false
    return $bound
}
function Invoke-FrameworkCollection {
    param($Plan,[switch]$Execute,[scriptblock]$UnitAdapter,[scriptblock]$NativeAdapter)
    Require-FrameworkFact ($Plan.schemaVersion -eq 1) 'E_PLAN' 'Unsupported collection plan.'
    if($Execute){
        Require-FrameworkFact ($null -ne $NativeAdapter -and $null -eq $UnitAdapter) 'E_PREPARATION_GATE' 'Actual execution requires a released native adapter, never the unit adapter.'
        return Invoke-FrameworkNativeCollection -Plan $Plan -NativeAdapter $NativeAdapter
    }
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
function Invoke-FrameworkNativeCollection {
    param($Plan,[Parameter(Mandatory)][scriptblock]$NativeAdapter)
    Assert-FrameworkEffectsReleased
    $lease=(Get-Variable FrameworkNativeQualification -Scope Script).Value;$context=$lease.context
    Require-FrameworkFact ($context.scope -ceq 'NativeExecution' -and $lease.executionGatePath -and
        $Plan.schemaVersion -eq 1 -and $Plan.runId -ceq $context.runId -and $Plan.platform -ceq $context.platform -and
        $Plan.binding.playerBuildId -ceq $context.playerBuildId -and $Plan.binding.archiveSha256 -ceq $lease.archiveSha256 -and
        [IO.Path]::GetFullPath($Plan.binding.runtimePlayerRoot) -ceq $lease.binding.runtimePlayerRoot -and $Plan.cases.Count -gt 0) 'E_NATIVE_COLLECTION_BINDING' 'Native collection requires the current released original archive, run and platform.'
    $result=@{schemaVersion=1;evidenceKind='NativeCollection';nativeAcceptance=$false;runId=$Plan.runId;platform=$Plan.platform;playerBuildId=$context.playerBuildId;cases=@()}
    foreach($case in $Plan.cases){
        $record=@{id=$case.id;taskId=$case.taskId;status='NotRun';executed=$false;attempts=@();error=$null};$owned=$null;$terminal=$null
        try{
            $record.preparation=& $NativeAdapter 'Prepare' $case $case.attempts[0] $null
            foreach($attempt in $case.attempts){
                $owned=& $NativeAdapter 'Start' $case $attempt $null;$terminal=$null
                Require-FrameworkFact ((Test-FrameworkInteger $owned.processId) -and $owned.processId -gt 0 -and $owned.runId -ceq $Plan.runId -and $owned.caseId -ceq $case.id) 'E_NATIVE_COLLECTION_OWNER' 'Adapter did not bind an actual owned process to this logical case.'
                foreach($prior in $record.attempts){Require-FrameworkFact ($prior.processId -ne $owned.processId) 'E_NATIVE_COLLECTION_RESTART' 'Recovery must use a complete new native PID.'}
                if($case.kind -ceq 'strong-kill' -and $attempt.role -ceq 'primary'){
                    $checkpoint=& $NativeAdapter 'WaitCheckpoint' $case $attempt $owned
                    $options=ConvertFrom-FrameworkJson $attempt.command.acceptanceJson -ObjectOnly
                    Assert-FrameworkCheckpoint $checkpoint @{runId=$Plan.runId;caseId=$case.id;processId=$owned.processId;acceptance=$options} $checkpoint.attemptId
                    $record.kill=& $NativeAdapter 'KillOwned' $case $attempt $owned
                }elseif($case.kind -ceq 'ui'){$record.operator=& $NativeAdapter 'ExerciseUi' $case $attempt $owned}
                elseif($case.kind -ceq 'session'){$record.server=& $NativeAdapter 'ExerciseSession' $case $attempt $owned}
                $terminal=& $NativeAdapter 'WaitTerminal' $case $attempt $owned
                Require-FrameworkFact ($terminal.terminal -is [bool] -and $terminal.terminal -eq $true -and $terminal.processId -eq $owned.processId) 'E_NATIVE_COLLECTION_TERMINAL' 'Actual owned process is not terminal.'
                $capture=& $NativeAdapter 'Collect' $case $attempt $owned
                Require-FrameworkFact ($capture.evidenceKind -ceq 'Native' -and $capture.schemaAccepted -eq $true -and $capture.receipt.path -is [string] -and $capture.receipt.sha256 -cmatch '^[a-f0-9]{64}$' -and (Get-FrameworkFileHash $capture.receipt.path) -ceq $capture.receipt.sha256) 'E_NATIVE_COLLECTION_CAPTURE' 'Original file-bound native capture receipt is required.'
                $descriptor=Read-FrameworkJson $capture.receipt.path;$directory=Split-Path -Parent $capture.receipt.path
                Require-FrameworkFact ($descriptor.evidenceKind -ceq 'Native' -and $descriptor.schemaAccepted -eq $true -and $descriptor.processId -eq $owned.processId -and $descriptor.attemptId -cmatch '^[a-f0-9]{32}$') 'E_NATIVE_COLLECTION_CAPTURE' 'Capture descriptor is synthetic, rejected or belongs to another process.'
                $events=Read-FrameworkEvents (Resolve-FrameworkReference $directory $descriptor.events)
                $process=Read-FrameworkReference $directory $descriptor.process -Json
                Assert-FrameworkAttemptEnvelope @{caseId=$case.id;attemptId=$descriptor.attemptId;processId=$owned.processId;observedProcessId=$owned.processId;process=$process;events=$events} @{runId=$Plan.runId;playerBuildId=$context.playerBuildId} $case.id
                foreach($prior in $record.attempts){Require-FrameworkFact ($prior.attemptId -cne $descriptor.attemptId) 'E_NATIVE_COLLECTION_RESTART' 'Native-generated recovery attempt was reused.'}
                $record.attempts+=@{role=$attempt.role;processId=$owned.processId;attemptId=$descriptor.attemptId;capture=$capture;terminal=$terminal}
                $owned=$null
            }
            # 这里只表示原始证据已采集；每个故障/界面断言和完整矩阵仍由判定器决定。
            $record.executed=$true;$record.status='Collected'
        }catch{
            $record.error=$_.Exception.ToString();$record.status='Fail'
            if($owned -and -not ($terminal -and $terminal.terminal -eq $true)){
                try{$record.cleanup=& $NativeAdapter 'CleanupOwned' $case $attempt $owned}catch{$record.cleanupError=$_.Exception.ToString()}
            }
        }
        $result.cases+=$record
    }
    return $result
}
