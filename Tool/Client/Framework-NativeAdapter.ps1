#requires -Version 7.0
# Native effects remain closed except inside an archive-bound qualification scope.
. "$PSScriptRoot/Framework-TransferPolicy.ps1"
function Invoke-FrameworkNativeQualification {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Windows64','Android')][string]$Platform,
        [Parameter(Mandatory)][string]$RunId,[Parameter(Mandatory)][string]$PlayerArchive,
        [Parameter(Mandatory)][string]$BuildResult,[Parameter(Mandatory)][string]$RuntimePlayerRoot,
        [Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][scriptblock]$Body,
        [string]$DeviceId)
    $prior=Get-Variable FrameworkNativeQualification -Scope Script -ErrorAction SilentlyContinue
    Require-FrameworkFact (-not $prior -or -not $prior.Value.active) 'E_QUALIFICATION_NESTED' 'An active native qualification owner cannot be replaced.'
    Require-FrameworkFact ($RunId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and ($Platform -cne 'Android' -or $DeviceId -cmatch '^[A-Za-z0-9_.:-]+$')) 'E_QUALIFICATION_IDENTITY' 'A bounded run and explicit Android device are required.'
    $archive=Read-FrameworkArchive $PlayerArchive $BuildResult $Platform
    $nativeRoot=[IO.Path]::GetFullPath($RuntimePlayerRoot)
    $before=@{};foreach($item in (Get-FrameworkInventory $nativeRoot).GetEnumerator()){$before[$item.Key]=$item.Value.sha256}
    Assert-FrameworkHashMap $archive.archiveNative $before 'Qualification runnable native package'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $context=@{scope='QualificationOnly';nativeAcceptance=$false;platform=$Platform;runId=$RunId;deviceId=$DeviceId;
        playerBuildId=$archive.playerBuildId;profilePackageId=$archive.profilePackageId;updateEndpoint=$archive.updateEndpoint;
        archiveNative=$archive.archiveNative;runtimePlayerRoot=$nativeRoot;nativeBinaryEvidence=$archive.nativeBinaryEvidence}
    $binding=@{platform=$Platform;runId=$RunId;deviceId=$DeviceId;playerBuildId=$archive.playerBuildId;runtimePlayerRoot=$nativeRoot}
    $script:FrameworkNativeQualification=@{active=$true;ownerProcessId=$PID;context=$context;binding=$binding;
        archivePath=(Join-Path ([IO.Path]::GetFullPath($PlayerArchive)) 'archive.json');archiveSha256=$archive.archiveSha256}
    $bodyError=$null;$mutation=$null;$value=$null;$after=@{};$unchanged=$false
    try{$value=& $Body $context}catch{$bodyError=$_}
    finally{
        # 资格检测只开放当前调用栈；正常返回、异常和校验失败均先关门，再保存结果。
        $script:FrameworkNativeQualification.active=$false
        $script:FrameworkNativeQualification=$null
        try{
            foreach($item in (Get-FrameworkInventory $nativeRoot).GetEnumerator()){$after[$item.Key]=$item.Value.sha256}
            Assert-FrameworkHashMap $before $after 'Qualification native package after execution'
            $original=Read-FrameworkArchive $PlayerArchive $BuildResult $Platform
            Require-FrameworkFact ($original.archiveSha256 -ceq $archive.archiveSha256 -and $original.buildResultSha256 -ceq $archive.buildResultSha256) 'E_QUALIFICATION_ARCHIVE_CHANGED' 'Original build identity changed during qualification.'
            $unchanged=$true
        }catch{$mutation=$_}
        $result=@{schemaVersion=1;scope='QualificationOnly';nativeAcceptance=$false;platform=$Platform;runId=$RunId;
            playerBuildId=$archive.playerBuildId;archiveSha256=$archive.archiveSha256;buildResultSha256=$archive.buildResultSha256;
            nativeBefore=$before;nativeAfter=$after;nativeUnchanged=$unchanged;succeeded=($null -eq $bodyError -and $unchanged);
            error=$(if($bodyError){$bodyError.Exception.ToString()}else{$null});mutationError=$(if($mutation){$mutation.Exception.ToString()}else{$null});
            output=$value;finishedUtc=[DateTime]::UtcNow.ToString('o')}
        Write-FrameworkJsonNew (Join-Path $output 'qualification-result.json') $result
    }
    if($bodyError){throw $bodyError}
    if(-not $unchanged){throw [IO.InvalidDataException]::new('E_QUALIFICATION_NATIVE_CHANGED: Native or original archive bytes changed; see preserved qualification receipt.')}
    return $result
}
function Assert-FrameworkEffectsReleased {
    $variable=Get-Variable FrameworkNativeQualification -Scope Script -ErrorAction SilentlyContinue
    $lease=if($variable){$variable.Value}else{$null}
    $valid=$null -ne $lease -and $lease.active -eq $true -and $lease.ownerProcessId -eq $PID
    if($valid){foreach($field in @('platform','runId','deviceId','playerBuildId','runtimePlayerRoot')){if($lease.context[$field] -cne $lease.binding[$field]){$valid=$false;break}}}
    if($valid){$valid=(Get-FrameworkFileHash $lease.archivePath) -ceq $lease.archiveSha256}
    Require-FrameworkFact $valid 'E_PREPARATION_GATE' 'Native effects require the current owned archive-bound qualification scope; preparation and acceptance remain closed.'
}
function Assert-FrameworkOwnedProcess($Expected,$Observed) {
    foreach($field in @('processId','startedUtc','executable','executableSha256','runId','caseId','playerBuildId')){
        Require-FrameworkFact ($Expected.Contains($field) -and $Observed.Contains($field) -and $null -ne $Expected[$field]) 'E_OWNED_PROCESS' "Owned process field is missing: $field"
        Require-FrameworkFact ($Expected[$field] -ceq $Observed[$field]) 'E_OWNED_PROCESS' "Recycled or foreign process differs: $field"
    }
    Require-FrameworkFact ((Test-FrameworkInteger $Expected.processId) -and $Expected.processId -gt 0 -and $Expected.executableSha256 -cmatch '^[a-f0-9]{64}$') 'E_OWNED_PROCESS' 'Owned OS PID/executable SHA256 is invalid.'
    $null=[DateTimeOffset]::Parse($Expected.startedUtc)
}
function Assert-FrameworkCleanInstallPath([string]$NativeRoot,[string]$ApplicationDataRoot,[string]$PlayerBuildId,[string]$BackupRoot) {
    Require-FrameworkFact ($PlayerBuildId -cmatch '^[a-f0-9]{32}$') 'E_INSTALL_PATH' 'Only the original generated PlayerBuildId may identify an initial cache reset.'
    $actual=[IO.Path]::GetFullPath($NativeRoot);$data=[IO.Path]::GetFullPath($ApplicationDataRoot);$backup=[IO.Path]::GetFullPath($BackupRoot)
    $expected=[IO.Path]::GetFullPath((Join-Path $data ('Releases/'+$PlayerBuildId)))
    Require-FrameworkFact ($actual.Equals($expected,[StringComparison]::OrdinalIgnoreCase)) 'E_INSTALL_PATH' 'Initial reset must target exactly the observed Player release root, never its parent or another Player.'
    Require-FrameworkFact (-not $backup.Equals($actual,[StringComparison]::OrdinalIgnoreCase) -and -not $backup.StartsWith($actual+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and -not $actual.StartsWith($backup+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_INSTALL_PATH' 'Backup and native source roots must not overlap.'
    Assert-FrameworkNoReparse $actual;Assert-FrameworkNoReparse $backup
}
function Prepare-FrameworkWindowsBaseline($Context,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Windows64' -and $Context.installationPhase -ceq 'BeforeBaseline' -and (Test-FrameworkInteger $Context.completedRequiredCases) -and $Context.completedRequiredCases -eq 0 -and $Context.ownedNativeProcessesTerminal -is [bool] -and $Context.ownedNativeProcessesTerminal -eq $true -and $Context.nativeIdentity.player.PlayerBuildId -ceq $Context.playerBuildId) 'E_INITIAL_INSTALL' 'Only initial clean installation, before every required case and with the native lease released, may reset this exact owned cache.'
    $nativeRoot=[IO.Path]::GetFullPath($Context.nativeIdentity.releaseRoot)
    $data=[IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($nativeRoot))
    $localLow=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'AppData/LocalLow'))
    Require-FrameworkFact ($data.StartsWith($localLow+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_INITIAL_INSTALL' 'Observed native data root must remain under this user LocalLow tree.'
    $backup=[IO.Path]::GetFullPath((Join-Path $OutputDirectory 'prior-native-root'))
    Assert-FrameworkCleanInstallPath $nativeRoot $data $Context.playerBuildId $backup
    $output=New-FrameworkOutputDirectory $OutputDirectory;$files=@()
    if(Test-Path -LiteralPath $nativeRoot -PathType Container){
        foreach($directory in Get-ChildItem -LiteralPath $nativeRoot -Recurse -Directory -Force){Assert-FrameworkNoReparse $directory.FullName}
        foreach($file in Get-ChildItem -LiteralPath $nativeRoot -Recurse -File){
            Assert-FrameworkNoReparse $file.FullName;$relative=$file.FullName.Substring($nativeRoot.Length+1);$copy=Join-Path $backup $relative;[IO.Directory]::CreateDirectory((Split-Path -Parent $copy))|Out-Null
            $hash=Get-FrameworkFileHash $file.FullName;[IO.File]::Copy($file.FullName,$copy,$false)
            Require-FrameworkFact ((Get-FrameworkFileHash $copy) -ceq $hash -and (Get-FrameworkFileHash $file.FullName) -ceq $hash) 'E_INITIAL_BACKUP' 'Prior task-owned native bytes changed during preservation.'
            $files+=@{path=$relative;sha256=$hash}
        }
        Write-FrameworkJsonNew (Join-Path $output 'prior-native-root.json') @{originalRoot=$nativeRoot;playerBuildId=$Context.playerBuildId;files=$files}
        $after=@(Get-ChildItem -LiteralPath $nativeRoot -Recurse -File)
        Require-FrameworkFact ($after.Count -eq $files.Count) 'E_INITIAL_BACKUP' 'Native files changed after preservation; no initial cleanup is permitted.'
        foreach($entry in $files){Require-FrameworkFact ((Get-FrameworkFileHash (Join-Path $nativeRoot $entry.path)) -ceq $entry.sha256) 'E_INITIAL_BACKUP' 'Native bytes changed after preservation; no initial cleanup is permitted.'}
        # Recheck the resolved absolute target immediately before the sole initial recursive deletion.
        Assert-FrameworkCleanInstallPath $nativeRoot $data $Context.playerBuildId $backup
        [IO.Directory]::Delete($nativeRoot,$true)
    }
    Write-FrameworkJsonNew (Join-Path $output 'initial-install.json') @{runId=$Context.runId;playerBuildId=$Context.playerBuildId;clean=$true;count=1;priorRoot=$nativeRoot;preservedFiles=$files;phase='BeforeBaseline'}
    $Context.installationPhase='BaselinePrepared'
}
function ConvertFrom-FrameworkProcStat([string]$Text) {
    $match=[regex]::Match($Text.Trim(),'^(?<pid>[1-9][0-9]*) \(.*\) (?<tail>.+)$')
    Require-FrameworkFact $match.Success 'E_ANDROID_PROCESS' 'Invalid raw /proc PID stat record.'
    $fields=$match.Groups['tail'].Value.Split(' ',[StringSplitOptions]::RemoveEmptyEntries)
    Require-FrameworkFact ($fields.Count -ge 20 -and $fields[19] -cmatch '^[0-9]+$') 'E_ANDROID_PROCESS' 'Kernel process start ticks are missing.'
    return @{processId=[long]$match.Groups['pid'].Value;startTicks=$fields[19]}
}
function Assert-FrameworkAndroidProcess($Expected,$Observed) {
    foreach($field in @('deviceId','processId','startTicks','bootId','applicationId')){Require-FrameworkFact ($Expected.Contains($field) -and $Observed.Contains($field) -and $null -ne $Expected[$field] -and $Expected[$field] -ceq $Observed[$field]) 'E_ANDROID_OWNER' "Foreign/recycled Android process: $field"}
    Require-FrameworkFact ((Test-FrameworkInteger $Expected.processId) -and $Expected.processId -gt 0 -and $Expected.startTicks -cmatch '^[0-9]+$' -and $Expected.bootId -cmatch '^[a-f0-9-]{36}$') 'E_ANDROID_OWNER' 'Actual Android process identity is incomplete.'
}
function Get-FrameworkReleaseSelection([string]$ReleaseRoot,$Receipt,[string]$RunId,[string]$CaseId) {
    Require-FrameworkFact ($Receipt.runId -ceq $RunId -and $Receipt.caseId -ceq $CaseId -and $Receipt.relativeDirectory -is [string] -and $Receipt.relativeDirectory -cnotmatch '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))' -and $Receipt.files -is [System.Collections.IList] -and $Receipt.files.Count -ge 4) 'E_RELEASE_SELECTION' 'Explicit run/case and complete selected published-input receipt are required.'
    $root=[IO.Path]::GetFullPath($ReleaseRoot);Assert-FrameworkNoReparse $root
    $selected=[IO.Path]::GetFullPath((Join-Path $root $Receipt.relativeDirectory));$prefix=$root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    Require-FrameworkFact ($selected.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) 'E_RELEASE_SELECTION' 'Published input escapes its owned catalog.'
    $paths=@();foreach($file in $Receipt.files){$path=Resolve-FrameworkReference $root $file;Require-FrameworkFact ($path.StartsWith($selected+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'E_RELEASE_SELECTION' 'Selected receipt includes another release directory.';$paths+=$path}
    foreach($name in @('channel.json','channel.sig','release.json','release.sig')){Require-FrameworkFact ((Join-Path $selected $name) -cin $paths) 'E_RELEASE_SELECTION' 'Signed envelope/manifest input is missing from the selected receipt.'}
    return @{runId=$RunId;caseId=$CaseId;releaseRoot=$root;relativeDirectory=$Receipt.relativeDirectory;files=$Receipt.files}
}
function Start-FrameworkReleaseServer($Command,$Context,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    $endpoint=[Uri]$Context.updateEndpoint
    Require-FrameworkFact ($endpoint.IsLoopback -and $endpoint.Scheme -ceq 'http' -and $endpoint.Port -ge 1024 -and $Command.fileSha256 -cmatch '^[a-f0-9]{64}$' -and (Get-FrameworkFileHash $Command.file) -ceq $Command.fileSha256 -and (Get-FrameworkFileHash $Command.script) -ceq $Command.scriptSha256) 'E_SERVER_RECEIPT' 'Owned update server tool/script and fixed loopback endpoint must match their receipts.'
    $control=New-FrameworkOutputDirectory $OutputDirectory
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Command.file;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('-NoProfile','-File',$Command.script,'-ReleaseRoot',$Context.releaseRoot,'-ControlRoot',$control,'-ListenAddress',$endpoint.Host,'-Port',[string]$endpoint.Port,'-ChunkDelayMilliseconds',[string]$Context.chunkDelayMilliseconds)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start);$record=@{processId=$process.Id;startedUtc=$process.StartTime.ToUniversalTime().ToString('o');executable=$process.MainModule.FileName;executableSha256=$Command.fileSha256;binarySha256=$Command.fileSha256;kind='ReleaseServer';runId=$Context.runId;caseId='update-server';playerBuildId=$Context.playerBuildId;scriptSha256=$Command.scriptSha256;releaseRoot=$Context.releaseRoot;controlRoot=$control;endpoint=$Context.updateEndpoint}
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path -LiteralPath (Join-Path $control 'server-ready'))){Require-FrameworkFact (-not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt 15) 'E_SERVER_START' 'Owned server did not publish readiness.';Start-Sleep -Milliseconds 50}
    Require-FrameworkFact (([IO.File]::ReadAllText((Join-Path $control 'server-ready'))).Trim() -ceq [string]$process.Id) 'E_SERVER_OWNER' 'Server-ready PID differs from the held helper.'
    Write-FrameworkJsonNew (Join-Path $control 'server-process.json') $record
    return @{handle=$process;record=$record;outputDirectory=$control;stdout=$stdout;stderr=$stderr}
}
function Select-FrameworkReleaseInput($Server,$Selection) {
    Assert-FrameworkEffectsReleased
    $null=Get-FrameworkWindowsOwnedObservation $Server
    Require-FrameworkFact ($Selection.runId -ceq $Server.record.runId -and [IO.Path]::GetFullPath($Selection.releaseRoot) -ceq [IO.Path]::GetFullPath($Server.record.releaseRoot)) 'E_SERVER_SELECTION' 'Release selection belongs to a different server/run.'
    $selection=Get-FrameworkReleaseSelection $Selection.releaseRoot $Selection $Selection.runId $Selection.caseId
    Clear-FrameworkTransferPolicy $Server
    $temporary=Join-Path $Server.outputDirectory ('selection-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    $stream=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$bytes=[Text.Encoding]::UTF8.GetBytes($selection.relativeDirectory);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
    # This changes only the owned test server's input pointer, never native release state or signed input bytes.
    [IO.File]::Move($temporary,(Join-Path $Server.outputDirectory 'active-release.txt'),$true)
    Write-FrameworkJsonNew (Join-Path $Server.outputDirectory ('selected-'+$Selection.caseId+'.json')) $selection
}
function Clear-FrameworkTransferPolicy($Server) {
    Assert-FrameworkEffectsReleased
    $null=Get-FrameworkWindowsOwnedObservation $Server
    $root=[IO.Path]::GetFullPath($Server.outputDirectory);Assert-FrameworkNoReparse $root
    $current=Join-Path $root 'transfer-policy.json'
    if(Test-Path -LiteralPath $current -PathType Leaf){
        $retired=Join-Path $root ('retired-policy-'+[Guid]::NewGuid().ToString('N')+'.json')
        Require-FrameworkFact ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($current)) -ceq $root -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($retired)) -ceq $root) 'E_TRANSFER_POLICY_PATH' 'Policy retirement must stay in this owned server control directory.'
        # Preserve previous policy bytes. This only retires a test-service pointer, never native release state.
        [IO.File]::Move($current,$retired)
    }
}
function Set-FrameworkTransferPolicy($Server,$Selection,$Policy) {
    Assert-FrameworkEffectsReleased
    $null=Get-FrameworkWindowsOwnedObservation $Server
    Require-FrameworkFact ($Selection.runId -ceq $Server.record.runId -and $Policy.runId -ceq $Selection.runId -and $Policy.caseId -ceq $Selection.caseId) 'E_TRANSFER_OWNER' 'Hold policy differs from the selected server/run/case.'
    $selected=Get-FrameworkReleaseSelection $Selection.releaseRoot $Selection $Selection.runId $Selection.caseId
    Assert-FrameworkTransferPolicy $Policy (Join-Path $selected.releaseRoot $selected.relativeDirectory)
    Write-FrameworkJsonNew (Join-Path $Server.outputDirectory ('policy-'+$Policy.caseId+'.json')) $Policy
    $temporary=Join-Path $Server.outputDirectory ('policy-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    Write-FrameworkJsonNew $temporary $Policy
    [IO.File]::Move($temporary,(Join-Path $Server.outputDirectory 'transfer-policy.json'),$true)
}
function Complete-FrameworkHeldTransfer($Server,$Holding,$NativeOutcome,[ValidateSet('owned-kill','cancel','network-interruption')][string]$Reason) {
    Assert-FrameworkEffectsReleased
    $null=Get-FrameworkWindowsOwnedObservation $Server
    Require-FrameworkFact ($Holding.transferId -cmatch '^[a-f0-9]{32}$' -and $Holding.serverProcessId -eq $Server.record.processId -and $Holding.runId -ceq $Server.record.runId -and $NativeOutcome.runId -ceq $Holding.runId -and $NativeOutcome.caseId -ceq $Holding.caseId -and (Test-FrameworkInteger $NativeOutcome.processId) -and $NativeOutcome.processId -gt 0) 'E_TRANSFER_OWNER' 'Abort must bind this owned server transfer and actual native process.'
    if($Reason -ceq 'owned-kill'){Require-FrameworkFact ($NativeOutcome.terminal -eq $true -and $NativeOutcome.action -cin @('TerminateProcess','am force-stop')) 'E_TRANSFER_KILL' 'Owned native termination must complete before ending the held response.'}
    $directory=Join-Path $Server.outputDirectory ('transfers/'+$Holding.transferId)
    $original=Read-FrameworkJson (Join-Path $directory 'holding.json')
    Require-FrameworkFact (($original|ConvertTo-Json -Depth 20 -Compress) -ceq ($Holding|ConvertTo-Json -Depth 20 -Compress) -and -not(Test-Path -LiteralPath (Join-Path $directory 'terminal.json'))) 'E_TRANSFER_HOLD' 'Original response is no longer held or its receipt changed.'
    $control=@{runId=$Holding.runId;caseId=$Holding.caseId;transferId=$Holding.transferId;serverProcessId=$Holding.serverProcessId;nativeProcessId=$NativeOutcome.processId;reason=$Reason}
    Write-FrameworkJsonNew (Join-Path $directory 'abort.json') $control
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path -LiteralPath (Join-Path $directory 'terminal.json'))){Require-FrameworkFact (-not $Server.handle.HasExited -and $clock.Elapsed.TotalSeconds -lt 5) 'E_TRANSFER_ABORT' 'Server did not acknowledge abort of the held response.';Start-Sleep -Milliseconds 20}
    $terminal=Read-FrameworkJson (Join-Path $directory 'terminal.json')
    Require-FrameworkFact ($terminal.phase -ceq 'Abort' -and $terminal.reason -ceq $Reason -and $terminal.bytesSent -eq $Holding.bytesSent -and $terminal.completed -eq $false -and $terminal.nativeProcessId -eq $NativeOutcome.processId) 'E_TRANSFER_ABORT' 'Held stream resumed/completed or ended for another reason.'
    return @{holding=@{path=(Join-Path $directory 'holding.json');sha256=(Get-FrameworkFileHash (Join-Path $directory 'holding.json'))};abort=@{path=(Join-Path $directory 'terminal.json');sha256=(Get-FrameworkFileHash (Join-Path $directory 'terminal.json'))}}
}
function Stop-FrameworkReleaseServer($Server) {
    Assert-FrameworkEffectsReleased
    $null=Get-FrameworkWindowsOwnedObservation $Server
    $path=Join-Path $Server.outputDirectory 'server-stop';$stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read);$stream.Dispose()
    Require-FrameworkFact ($Server.handle.WaitForExit(15000)) 'E_SERVER_STOP' 'Owned server did not stop through its control file.'
    [IO.File]::WriteAllText((Join-Path $Server.outputDirectory 'server.stdout.log'),$Server.stdout.Result);[IO.File]::WriteAllText((Join-Path $Server.outputDirectory 'server.stderr.log'),$Server.stderr.Result)
    Write-FrameworkJsonNew (Join-Path $Server.outputDirectory 'server-terminal.json') @{processId=$Server.handle.Id;terminal=$Server.handle.HasExited;exitCode=$Server.handle.ExitCode;finishedUtc=[DateTime]::UtcNow.ToString('o')}
}
function Get-FrameworkWindowsOwnedObservation($Owned) {
    Assert-FrameworkEffectsReleased
    $process=$Owned.handle
    Require-FrameworkFact ($process -is [Diagnostics.Process] -and -not $process.HasExited) 'E_OWNED_PROCESS' 'The original held process must still be live.'
    $actual=$Owned.record.Clone();$actual.processId=$process.Id;$actual.startedUtc=$process.StartTime.ToUniversalTime().ToString('o');$actual.executable=$process.MainModule.FileName;$actual.executableSha256=Get-FrameworkFileHash $actual.executable
    Assert-FrameworkOwnedProcess $Owned.record $actual
    return $actual
}
function Start-FrameworkWindowsAttempt($Command,$Context,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Windows64' -and $Context.archiveNative['Client.exe'] -ceq $Command.sha256 -and (Get-FrameworkFileHash $Command.file) -ceq $Command.sha256) 'E_NATIVE_EXECUTABLE' 'Windows launch is not bound to the original archive executable.'
    $proof=Get-FrameworkBinaryEvidence $Command.file PE
    $options=ConvertFrom-FrameworkJson $Command.acceptanceJson -ObjectOnly
    Require-FrameworkFact ($options.runId -ceq $Context.runId -and $options.caseId -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_NATIVE_OPTIONS' 'Launch run/case differs from its bound context.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=[IO.Path]::GetFullPath($Command.file);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    foreach($argument in $Command.arguments){$start.ArgumentList.Add($argument)}
    $start.ArgumentList.Add('-logFile');$start.ArgumentList.Add((Join-Path $output 'Player.log'))
    $process=[Diagnostics.Process]::Start($start)
    $record=@{processId=$process.Id;startedUtc=$process.StartTime.ToUniversalTime().ToString('o');executable=$process.MainModule.FileName;executableSha256=$proof.sha256;runId=$options.runId;caseId=$options.caseId;playerBuildId=$Context.playerBuildId;acceptance=$options;arguments=@($start.ArgumentList)}
    Write-FrameworkJsonNew (Join-Path $output 'process-start.json') $record
    return @{handle=$process;record=$record;command=$Command;outputDirectory=$output}
}
function Stop-FrameworkWindowsAttempt($Owned,$Checkpoint) {
    Assert-FrameworkEffectsReleased
    $actual=Get-FrameworkWindowsOwnedObservation $Owned
    $options=ConvertFrom-FrameworkJson $Owned.command.acceptanceJson -ObjectOnly
    Require-FrameworkFact ($Checkpoint.runId -ceq $actual.runId -and $Checkpoint.caseId -ceq $actual.caseId -and $Checkpoint.processId -eq $actual.processId -and $Checkpoint.attemptId -cmatch '^[a-f0-9]{32}$' -and $Checkpoint.point -ceq $options.pausePoint -and $Checkpoint.purpose -ceq $options.statePurpose) 'E_KILL_OWNERSHIP' 'Strong kill requires the exact owned native checkpoint.'
    if($Checkpoint.point -ceq 'download.partial'){Require-FrameworkFact ($Checkpoint.data.bytes -gt 0 -and $Checkpoint.data.expectedBytes -gt $Checkpoint.data.bytes) 'E_PARTIAL_TRANSFER' 'Transfer must be positive and incomplete.'}
    $when=[DateTime]::UtcNow.ToString('o');$Owned.handle.Kill();$Owned.handle.WaitForExit()
    $record=@{runId=$actual.runId;caseId=$actual.caseId;processId=$actual.processId;attemptId=$Checkpoint.attemptId;point=$Checkpoint.point;utc=$when;action='TerminateProcess';terminal=$Owned.handle.HasExited;exitCode=$Owned.handle.ExitCode;processStartUtc=$actual.startedUtc;executableSha256=$actual.executableSha256}
    Write-FrameworkJsonNew (Join-Path $Owned.outputDirectory 'kill.json') $record
    return $record
}
function Wait-FrameworkWindowsAttempt($Owned,[ValidateRange(1,300)][int]$TimeoutSeconds=120) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Owned.handle -is [Diagnostics.Process]) 'E_OWNED_PROCESS' 'Original process handle is required.'
    Require-FrameworkFact ($Owned.handle.WaitForExit($TimeoutSeconds*1000)) 'E_NATIVE_TIMEOUT' 'Owned native attempt did not terminate; it is not successful evidence.'
    $result=$Owned.record.Clone();$result.terminal=$Owned.handle.HasExited;$result.exitCode=$Owned.handle.ExitCode;$result.finishedUtc=[DateTime]::UtcNow.ToString('o')
    Write-FrameworkJsonNew (Join-Path $Owned.outputDirectory 'process-terminal.json') $result
    return $result
}
function Wait-FrameworkNativeCheckpoint($Owned,[string]$NativeCaseDirectory,[ValidateRange(1,90)][int]$TimeoutSeconds=60) {
    Assert-FrameworkEffectsReleased
    $options=ConvertFrom-FrameworkJson $Owned.command.acceptanceJson -ObjectOnly
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($options.pausePoint)) 'E_CHECKPOINT_REQUEST' 'A declared native pause boundary is required.'
    Assert-FrameworkNoReparse $NativeCaseDirectory
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do{
        if(Test-Path -LiteralPath $NativeCaseDirectory -PathType Container){
            foreach($directory in Get-ChildItem -LiteralPath $NativeCaseDirectory -Directory){
                Require-FrameworkFact ($directory.Name -cmatch '^[a-f0-9]{32}$') 'E_ATTEMPT_DIRECTORY' 'Unexpected native attempt directory.'
                $path=Join-Path $directory.FullName 'checkpoint.json'
                if(-not (Test-Path -LiteralPath $path -PathType Leaf)){continue}
                try{$checkpoint=Read-FrameworkJson $path}catch [IO.IOException]{continue}
                if($checkpoint.processId -ne $Owned.record.processId){continue}
                Require-FrameworkFact ($checkpoint.attemptId -ceq $directory.Name -and $checkpoint.runId -ceq $options.runId -and $checkpoint.caseId -ceq $options.caseId -and $checkpoint.point -ceq $options.pausePoint -and $checkpoint.purpose -ceq $options.statePurpose) 'E_CHECKPOINT_IDENTITY' 'Native checkpoint does not bind the original owned request.'
                $token="$($options.runId):$($options.caseId):$($directory.Name):$($Owned.record.processId):$($options.pausePoint):$($options.statePurpose)"
                Require-FrameworkFact ($checkpoint.token -ceq $token) 'E_CHECKPOINT_TOKEN' 'Native-generated checkpoint token differs.'
                $Owned.record.attemptId=$checkpoint.attemptId
                return $checkpoint
            }
        }
        Require-FrameworkFact (-not $Owned.handle.HasExited) 'E_CHECKPOINT_PROCESS' 'Owned process exited before the required boundary.'
        Start-Sleep -Milliseconds 50
    }while($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw [IO.InvalidDataException]::new('E_CHECKPOINT_TIMEOUT: Required native boundary was not observed; no strong-kill proof is produced.')
}
function Assert-FrameworkCheckpoint($Checkpoint,$Owned,[string]$AttemptId) {
    Assert-FrameworkFields $Checkpoint @('runId','caseId','attemptId','processId','point','purpose','token','data')
    Require-FrameworkFact ($AttemptId -cmatch '^[a-f0-9]{32}$' -and $Checkpoint.attemptId -ceq $AttemptId -and
        $Checkpoint.runId -ceq $Owned.runId -and $Checkpoint.caseId -ceq $Owned.caseId -and
        (Test-FrameworkInteger $Checkpoint.processId) -and $Checkpoint.processId -eq $Owned.processId -and
        $Checkpoint.point -ceq $Owned.acceptance.pausePoint -and $Checkpoint.purpose -ceq $Owned.acceptance.statePurpose) 'E_CHECKPOINT_IDENTITY' 'Native checkpoint differs from the owned process, attempt or requested boundary.'
    $token="$($Owned.runId):$($Owned.caseId):${AttemptId}:$($Owned.processId):$($Owned.acceptance.pausePoint):$($Owned.acceptance.statePurpose)"
    Require-FrameworkFact ($Checkpoint.token -ceq $token) 'E_CHECKPOINT_TOKEN' 'Native checkpoint token differs from the original request.'
    if($Checkpoint.point -ceq 'download.partial'){
        Require-FrameworkFact ($Checkpoint.data -is [Collections.IDictionary] -and (Test-FrameworkInteger $Checkpoint.data.bytes) -and
            (Test-FrameworkInteger $Checkpoint.data.expectedBytes) -and $Checkpoint.data.bytes -gt 0 -and $Checkpoint.data.bytes -lt $Checkpoint.data.expectedBytes) 'E_PARTIAL_TRANSFER' 'Strong kill requires a positive incomplete authenticated transfer.'
    }
}
function Wait-FrameworkAndroidCheckpoint($Tool,$Owned,$Context,[string]$OutputDirectory,[ValidateRange(1,60)][int]$TimeoutSeconds=60) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Android' -and $Owned.deviceId -ceq $Context.deviceId -and
        $Owned.applicationId -ceq $Context.profilePackageId -and $Owned.playerBuildId -ceq $Context.playerBuildId -and
        $Owned.runId -ceq $Context.runId -and $Owned.runId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and
        $Owned.caseId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $Owned.acceptance.pausePoint -cin @('download.partial','rename.before','state.replace.before','state.replace.after','trial.persisted','hotentry.executing')) 'E_ANDROID_CHECKPOINT' 'Checkpoint wait must bind the selected installed package and original bounded request.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $remote='/sdcard/Android/data/'+$Context.profilePackageId+'/files/Acceptance/'+$Owned.runId+'/'+$Owned.caseId
    $clock=[Diagnostics.Stopwatch]::StartNew();$iteration=0
    do{
        $poll=Join-Path $output ('poll-'+(++$iteration));[IO.Directory]::CreateDirectory($poll)|Out-Null
        $observed=Get-FrameworkAndroidObservation $Tool $Owned.deviceId $Owned.applicationId (Join-Path $poll 'process')
        Assert-FrameworkAndroidProcess $Owned $observed
        $listing=Invoke-FrameworkAdb $Tool $Owned.deviceId @('shell','ls','-1',$remote) (Join-Path $poll 'attempts')
        if($listing.exitCode -eq 0){
            $names=(Read-FrameworkUtf8 (Join-Path $listing.outputDirectory 'stdout.bin') 4096).Split("`n",[StringSplitOptions]::RemoveEmptyEntries)
            foreach($name in $names){
                $attempt=$name.Trim();Require-FrameworkFact ($attempt -cmatch '^[a-f0-9]{32}$') 'E_ANDROID_CHECKPOINT_PATH' 'Unexpected native attempt directory.'
                $capture=Invoke-FrameworkAdb $Tool $Owned.deviceId @('shell','cat',($remote+'/'+$attempt+'/checkpoint.json')) (Join-Path $poll ('checkpoint-'+$attempt))
                if($capture.exitCode -ne 0){continue}
                $source=Join-Path $capture.outputDirectory 'stdout.bin'
                $checkpoint=ConvertFrom-FrameworkJson (Read-FrameworkUtf8 $source 256KB) -ObjectOnly
                Assert-FrameworkCheckpoint $checkpoint $Owned $attempt
                # 原始设备字节单独保存；只把已核对的 native attempt 绑定到持有的进程记录。
                $copy=Join-Path $output 'native-checkpoint.json';[IO.File]::Copy($source,$copy,$false)
                $Owned.attemptId=$attempt
                Write-FrameworkJsonNew (Join-Path $output 'checkpoint-binding.json') @{runId=$Owned.runId;caseId=$Owned.caseId;attemptId=$attempt;processId=$Owned.processId;startTicks=$Owned.startTicks;bootId=$Owned.bootId;sourceCommand=$capture;checkpoint=@{path='native-checkpoint.json';sha256=(Get-FrameworkFileHash $copy)}}
                return $checkpoint
            }
        }
        Start-Sleep -Milliseconds 100
    }while($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw [IO.InvalidDataException]::new('E_CHECKPOINT_TIMEOUT: No owned Android checkpoint was observed; no kill proof is produced.')
}
function Invoke-FrameworkReceiptCommand($Command,[string]$OutputDirectory,[ValidateRange(1,300)][int]$TimeoutSeconds=120) {
    Assert-FrameworkEffectsReleased
    Assert-FrameworkNoReparse $Command.file
    Require-FrameworkFact ($Command.sha256 -cmatch '^[a-f0-9]{64}$' -and (Get-FrameworkFileHash $Command.file) -ceq $Command.sha256 -and $Command.arguments -is [System.Collections.IList]) 'E_TOOL_RECEIPT' 'External tool must match its explicit original file/argument receipt.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Command.file;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Command.arguments){Require-FrameworkFact ($argument -is [string]) 'E_TOOL_ARGUMENT' 'Literal string arguments are required.';$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start);$started=$process.StartTime.ToUniversalTime().ToString('o')
    $stream=[IO.FileStream]::new((Join-Path $output 'stdout.bin'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $stdout=$process.StandardOutput.BaseStream.CopyToAsync($stream);$stderr=$process.StandardError.ReadToEndAsync()
    try{
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){
            # Only this exact held helper process is stopped. No process-tree or device-wide cleanup is performed.
            Require-FrameworkFact ($process.StartTime.ToUniversalTime().ToString('o') -ceq $started) 'E_TOOL_OWNER' 'Helper process identity changed.'
            $process.Kill();$process.WaitForExit();throw [IO.InvalidDataException]::new('E_TOOL_TIMEOUT: Owned helper timed out.')
        }
        $null=$stdout.GetAwaiter().GetResult();$stream.Flush($true)
        # Windows 的文件共享规则要求先结束写句柄，再用独立读句柄计算完整捕获文件的哈希。
        $stream.Dispose()
        [IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$stderr.Result)
        $result=@{file=$Command.file;sha256=$Command.sha256;arguments=$Command.arguments;outputDirectory=$output;processId=$process.Id;startedUtc=$started;finishedUtc=[DateTime]::UtcNow.ToString('o');terminal=$process.HasExited;exitCode=$process.ExitCode;stdout=@{path='stdout.bin';sha256=(Get-FrameworkFileHash (Join-Path $output 'stdout.bin'))};stderr=@{path='stderr.log';sha256=(Get-FrameworkFileHash (Join-Path $output 'stderr.log'))}}
        Write-FrameworkJsonNew (Join-Path $output 'command-result.json') $result
        return $result
    }finally{$stream.Dispose();$process.Dispose()}
}
function Invoke-FrameworkAdb($Tool,[string]$DeviceId,[string[]]$Arguments,[string]$OutputFile) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($DeviceId -cmatch '^[A-Za-z0-9_.:-]+$' -and $Arguments.Count -gt 0 -and $Arguments[0] -cin @('get-state','shell','exec-out','pull','install','reverse','logcat')) 'E_ADB_SCOPE' 'An explicit selected device and supported task operation are required.'
    $command=@{file=$Tool.file;sha256=$Tool.sha256;arguments=(@('-s',$DeviceId)+$Arguments)}
    # stdout stays binary so APK/native captures and screenshots are never decoded and re-encoded.
    return Invoke-FrameworkReceiptCommand $command $OutputFile
}
function Get-FrameworkAndroidObservation($Tool,[string]$DeviceId,[string]$ApplicationId,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($ApplicationId -cmatch '^[A-Za-z0-9]+(\.[A-Za-z0-9]+)+$') 'E_ANDROID_PACKAGE' 'Exact baked application ID is required.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $pidResult=Invoke-FrameworkAdb $Tool $DeviceId @('shell','pidof',$ApplicationId) (Join-Path $output 'pid')
    Require-FrameworkFact ($pidResult.exitCode -eq 0) 'E_ANDROID_PID' 'Selected package has no running process.'
    $pidText=(Read-FrameworkUtf8 (Join-Path $pidResult.outputDirectory 'stdout.bin') 256).Trim()
    Require-FrameworkFact ($pidText -cmatch '^[1-9][0-9]*$') 'E_ANDROID_PID' 'Exactly one owned package PID is required.'
    $stat=Invoke-FrameworkAdb $Tool $DeviceId @('shell','cat',('/proc/'+$pidText+'/stat')) (Join-Path $output 'stat')
    $boot=Invoke-FrameworkAdb $Tool $DeviceId @('shell','cat','/proc/sys/kernel/random/boot_id') (Join-Path $output 'boot')
    $commandLine=Invoke-FrameworkAdb $Tool $DeviceId @('shell','cat',('/proc/'+$pidText+'/cmdline')) (Join-Path $output 'cmdline')
    Require-FrameworkFact ($stat.exitCode -eq 0 -and $boot.exitCode -eq 0 -and $commandLine.exitCode -eq 0) 'E_ANDROID_PROCESS' 'Raw process identity capture failed.'
    $process=ConvertFrom-FrameworkProcStat (Read-FrameworkUtf8 (Join-Path $stat.outputDirectory 'stdout.bin') 4096)
    $actualName=(Read-FrameworkUtf8 (Join-Path $commandLine.outputDirectory 'stdout.bin') 4096).Trim([char]0)
    Require-FrameworkFact ($process.processId -eq [long]$pidText -and $actualName -ceq $ApplicationId) 'E_ANDROID_PROCESS' 'PID command line is not the selected application.'
    $record=@{deviceId=$DeviceId;processId=$process.processId;startTicks=$process.startTicks;bootId=(Read-FrameworkUtf8 (Join-Path $boot.outputDirectory 'stdout.bin') 256).Trim();applicationId=$actualName;commands=@($pidResult,$stat,$boot,$commandLine)}
    Write-FrameworkJsonNew (Join-Path $output 'android-process.json') $record
    return $record
}
function Start-FrameworkAndroidAttempt($Tool,$Command,$Context,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Android' -and $Context.deviceId -cmatch '^[A-Za-z0-9_.:-]+$' -and $Context.installedApkSha256 -ceq $Context.archiveNative['Client.apk'] -and $Context.installReceiptSha256 -cmatch '^[a-f0-9]{64}$') 'E_ANDROID_INSTALL' 'Exact task-owned installed package receipt must precede native launch.'
    $options=ConvertFrom-FrameworkJson $Command.acceptanceJson -ObjectOnly
    Require-FrameworkFact ($options.runId -ceq $Context.runId -and $Command.arguments[0] -ceq '-s' -and $Command.arguments[1] -ceq $Context.deviceId -and $Command.arguments -ccontains ($Context.profilePackageId+'/com.unity3d.player.UnityPlayerActivity') -and $Command.sha256 -ceq $Tool.sha256) 'E_ANDROID_LAUNCH' 'Android intent must bind selected device, original package, run and pinned tool.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $launch=Invoke-FrameworkReceiptCommand $Command (Join-Path $output 'launch')
    Require-FrameworkFact ($launch.exitCode -eq 0) 'E_ANDROID_LAUNCH' 'Actual Android intent command failed.'
    $observed=Get-FrameworkAndroidObservation $Tool $Context.deviceId $Context.profilePackageId (Join-Path $output 'process')
    $observed.runId=$options.runId;$observed.caseId=$options.caseId;$observed.playerBuildId=$Context.playerBuildId;$observed.acceptance=$options;$observed.installReceiptSha256=$Context.installReceiptSha256;$observed.launch=$launch
    Write-FrameworkJsonNew (Join-Path $output 'process-start.json') $observed
    return $observed
}
function Get-FrameworkInstalledAndroidPackage($Tool,$Context,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Android' -and $Context.profilePackageId -cmatch '^[A-Za-z0-9]+(\.[A-Za-z0-9]+)+$') 'E_ANDROID_PACKAGE' 'Original native application ID is required.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $query=Invoke-FrameworkAdb $Tool $Context.deviceId @('shell','pm','path',$Context.profilePackageId) (Join-Path $output 'package-path')
    $text=(Read-FrameworkUtf8 (Join-Path $query.outputDirectory 'stdout.bin') 4096).Trim()
    if([string]::IsNullOrWhiteSpace($text)){
        Require-FrameworkFact ($query.exitCode -in @(0,1)) 'E_ANDROID_PACKAGE' 'Package absence query failed.'
        $absent=@{deviceId=$Context.deviceId;applicationId=$Context.profilePackageId;present=$false;query=$query}
        Write-FrameworkJsonNew (Join-Path $output 'installed-package.json') $absent
        return $absent
    }
    $match=[regex]::Match($text,'^package:(/data/app/[A-Za-z0-9_./=+~-]+/base\.apk)$')
    Require-FrameworkFact ($query.exitCode -eq 0 -and $match.Success) 'E_ANDROID_PACKAGE' 'Expected exactly one original APK installation path for this package.'
    $capture=Invoke-FrameworkAdb $Tool $Context.deviceId @('exec-out','cat',$match.Groups[1].Value) (Join-Path $output 'apk')
    Require-FrameworkFact ($capture.exitCode -eq 0) 'E_ANDROID_PACKAGE' 'Installed APK byte capture failed.'
    $apk=Join-Path $capture.outputDirectory 'stdout.bin';$proof=Get-FrameworkApkEvidence $apk
    $record=@{deviceId=$Context.deviceId;applicationId=$Context.profilePackageId;present=$true;installedPath=$match.Groups[1].Value;apkSha256=$proof.sha256;nativeLibraries=$proof.libraries;query=$query;capture=$capture}
    Write-FrameworkJsonNew (Join-Path $output 'installed-package.json') $record
    return $record
}
function Install-FrameworkAndroidPackage($Tool,$Context,[string]$ApkPath,[string]$OutputDirectory) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Android' -and (Get-FrameworkFileHash $ApkPath) -ceq $Context.archiveNative['Client.apk']) 'E_ANDROID_INSTALL' 'Only the exact original native APK may be installed.'
    $output=New-FrameworkOutputDirectory $OutputDirectory
    $before=Get-FrameworkInstalledAndroidPackage $Tool $Context (Join-Path $output 'before')
    Require-FrameworkFact ($before.present -eq $false) 'E_ANDROID_INSTALL_EXISTS' 'Clean baseline requires an absent package. Preserve and remove an existing task-owned installation under separate controller authorization; never overwrite or clear it implicitly.'
    $install=Invoke-FrameworkAdb $Tool $Context.deviceId @('install',$ApkPath) (Join-Path $output 'install')
    Require-FrameworkFact ($install.exitCode -eq 0) 'E_ANDROID_INSTALL' 'Original APK installation failed.'
    $after=Get-FrameworkInstalledAndroidPackage $Tool $Context (Join-Path $output 'after')
    Require-FrameworkFact ($after.present -eq $true -and $after.apkSha256 -ceq $Context.archiveNative['Client.apk']) 'E_ANDROID_INSTALL' 'Installed APK differs from its exact native archive.'
    $receipt=@{deviceId=$Context.deviceId;applicationId=$Context.profilePackageId;playerBuildId=$Context.playerBuildId;clean=$true;count=1;apkSha256=$after.apkSha256;before=$before;install=$install;after=$after}
    Write-FrameworkJsonNew (Join-Path $output 'install-receipt.json') $receipt
    return $receipt
}
function Stop-FrameworkAndroidAttempt($Tool,$Owned,$Checkpoint,[string]$OutputDirectory,$Context) {
    Assert-FrameworkEffectsReleased
    Require-FrameworkFact ($Context.platform -ceq 'Android' -and $Owned.deviceId -ceq $Context.deviceId -and $Owned.applicationId -ceq $Context.profilePackageId -and $Owned.playerBuildId -ceq $Context.playerBuildId -and $Owned.installReceiptSha256 -ceq $Context.installReceiptSha256) 'E_ANDROID_KILL' 'Stop is not bound to this task-owned native package installation.'
    $actual=Get-FrameworkAndroidObservation $Tool $Owned.deviceId $Owned.applicationId (Join-Path $OutputDirectory 'before-stop')
    Assert-FrameworkAndroidProcess $Owned $actual
    Require-FrameworkFact ($Checkpoint.processId -eq $Owned.processId -and $Checkpoint.runId -ceq $Owned.runId -and $Checkpoint.caseId -ceq $Owned.caseId -and $Checkpoint.attemptId -ceq $Owned.attemptId -and $Checkpoint.point -ceq $Owned.acceptance.pausePoint -and $Checkpoint.purpose -ceq $Owned.acceptance.statePurpose) 'E_ANDROID_KILL' 'Strong kill checkpoint differs from the actual owned application process/request.'
    if($Checkpoint.point -ceq 'download.partial'){Require-FrameworkFact ($Checkpoint.data.bytes -gt 0 -and $Checkpoint.data.bytes -lt $Checkpoint.data.expectedBytes) 'E_PARTIAL_TRANSFER' 'Require an actual incomplete authenticated transfer.'}
    $utc=[DateTime]::UtcNow.ToString('o')
    $stop=Invoke-FrameworkAdb $Tool $Owned.deviceId @('shell','am','force-stop',$Owned.applicationId) (Join-Path $OutputDirectory 'force-stop')
    $after=Invoke-FrameworkAdb $Tool $Owned.deviceId @('shell','pidof',$Owned.applicationId) (Join-Path $OutputDirectory 'after-stop')
    Require-FrameworkFact ($stop.exitCode -eq 0 -and [string]::IsNullOrWhiteSpace((Read-FrameworkUtf8 (Join-Path $after.outputDirectory 'stdout.bin') 256))) 'E_ANDROID_KILL' 'Owned Android process did not terminate.'
    $record=@{runId=$Owned.runId;caseId=$Owned.caseId;processId=$Owned.processId;attemptId=$Owned.attemptId;point=$Checkpoint.point;utc=$utc;action='am force-stop';terminal=$true;deviceId=$Owned.deviceId;startTicks=$Owned.startTicks;bootId=$Owned.bootId;commands=@($stop,$after)}
    Write-FrameworkJsonNew (Join-Path $OutputDirectory 'kill.json') $record
    return $record
}
