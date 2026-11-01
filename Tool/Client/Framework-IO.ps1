#requires -Version 7.0
# File-only preparation/import helpers. Command construction deliberately never starts a process.
function Get-FrameworkFileHash([string]$Path) {(Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()}
function Read-FrameworkMachine($Stream,[ValidateSet('PE','ELF')][string]$Format) {
    $reader=[IO.BinaryReader]::new($Stream,[Text.Encoding]::UTF8,$true)
    try{
        if($Format -ceq 'PE'){
            $header=$reader.ReadBytes(64)
            Require-FrameworkFact ($header.Length -eq 64 -and $header[0] -eq 77 -and $header[1] -eq 90) 'E_PE_HEADER' 'Original Windows image lacks its DOS header.'
            $offset=[BitConverter]::ToInt32($header,60)
            Require-FrameworkFact ($Stream.CanSeek -and $offset -ge 64 -and $offset -le $Stream.Length-26) 'E_PE_HEADER' 'Invalid PE header offset.'
            $null=$Stream.Seek($offset,[IO.SeekOrigin]::Begin)
            Require-FrameworkFact ($reader.ReadUInt32() -eq 17744) 'E_PE_HEADER' 'Missing PE signature.'
            $machine=$reader.ReadUInt16();$null=$Stream.Seek($offset+24,[IO.SeekOrigin]::Begin);$magic=$reader.ReadUInt16()
            Require-FrameworkFact ($machine -eq 34404 -and $magic -eq 523) 'E_PE_MACHINE' 'Windows64 requires original AMD64 PE32+ machine headers.'
            return @{format='PE';machine=$machine;wordBits=64}
        }
        $header=$reader.ReadBytes(20)
        Require-FrameworkFact ($header.Length -eq 20 -and $header[0] -eq 127 -and $header[1] -eq 69 -and $header[2] -eq 76 -and $header[3] -eq 70 -and $header[4] -eq 2 -and $header[5] -eq 1) 'E_ELF_HEADER' 'Expected a 64-bit little-endian ELF image.'
        $machine=[BitConverter]::ToUInt16($header,18)
        Require-FrameworkFact ($machine -eq 183) 'E_ELF_MACHINE' 'Android package requires original AArch64 ELF machine headers.'
        return @{format='ELF';machine=$machine;wordBits=64}
    }finally{$reader.Dispose()}
}
function Get-FrameworkBinaryEvidence([string]$Path,[ValidateSet('PE','ELF')][string]$Format) {
    Assert-FrameworkNoReparse $Path
    $stream=[IO.File]::OpenRead($Path)
    try{$proof=Read-FrameworkMachine $stream $Format}finally{$stream.Dispose()}
    $proof.sha256=Get-FrameworkFileHash $Path;$proof.length=(Get-Item -LiteralPath $Path).Length
    return $proof
}
function Get-FrameworkApkEvidence([string]$Path) {
    Assert-FrameworkNoReparse $Path
    $archive=[IO.Compression.ZipFile]::OpenRead($Path);$libraries=@{}
    try{
        foreach($entry in $archive.Entries|Where-Object {$_.FullName.StartsWith('lib/',[StringComparison]::Ordinal) -and $_.FullName.EndsWith('.so',[StringComparison]::Ordinal)}){
            Require-FrameworkFact ($entry.FullName.StartsWith('lib/arm64-v8a/',[StringComparison]::Ordinal) -and $entry.FullName -cnotmatch '(^|/)\.\.(/|$)' -and -not $libraries.ContainsKey($entry.FullName)) 'E_APK_ABI' 'Unexpected or duplicate APK native ABI entry.'
            $stream=$entry.Open();try{$proof=Read-FrameworkMachine $stream ELF}finally{$stream.Dispose()}
            $stream=$entry.Open();$hash=[Security.Cryptography.SHA256]::Create();try{$proof.sha256=([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$hash.Dispose()}
            $proof.length=$entry.Length;$libraries[$entry.FullName]=$proof
        }
    }finally{$archive.Dispose()}
    Require-FrameworkFact ($libraries.ContainsKey('lib/arm64-v8a/libil2cpp.so') -and $libraries.ContainsKey('lib/arm64-v8a/libunity.so')) 'E_APK_ABI' 'Original IL2CPP and Unity ARM64 libraries are required.'
    return @{format='APK';sha256=(Get-FrameworkFileHash $Path);libraries=$libraries}
}
function Assert-FrameworkFields($Value,[string[]]$Required,[string[]]$Optional=@()) {
    Require-FrameworkFact ($Value -is [System.Collections.IDictionary]) 'E_SCHEMA' 'Expected an object.'
    foreach($name in $Required){Require-FrameworkFact ($Value.Contains($name)) 'E_SCHEMA' "Missing required field: $name"}
    foreach($name in $Value.Keys){Require-FrameworkFact ($name -cin ($Required+$Optional)) 'E_SCHEMA' "Unknown or case-aliased field: $name"}
}
function Assert-FrameworkNoReparse([string]$Path) {
    for($entry=[IO.Path]::GetFullPath($Path);$entry;$entry=[IO.Path]::GetDirectoryName($entry)){
        if(Test-Path -LiteralPath $entry){Require-FrameworkFact (((Get-Item -LiteralPath $entry -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'E_REPARSE' 'Evidence paths cannot traverse a reparse point.'}
    }
}
function Read-FrameworkUtf8([string]$Path,[long]$MaximumBytes=32MB) {
    Assert-FrameworkNoReparse $Path
    Require-FrameworkFact ((Get-Item -LiteralPath $Path -ErrorAction Stop).Length -le $MaximumBytes) 'E_JSON_SIZE' 'Evidence exceeds its byte budget.'
    try{return [Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($Path))}catch{throw [IO.InvalidDataException]::new('E_UTF8: Evidence is not valid UTF8.', $_.Exception)}
}
function Copy-FrameworkLiveEventSnapshot([string]$Source,[string]$Destination) {
    Assert-FrameworkNoReparse $Source;Assert-FrameworkNoReparse $Destination
    # 原生日志以 FileShare.Read 持续写入；读取端须允许现有写句柄，不能用 ReadAllBytes 的共享模式。
    $reader=[IO.FileStream]::new($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try{
        $remaining=$reader.Length;Require-FrameworkFact ($remaining -le 32MB) 'E_EVENTS_SIZE' 'Live event snapshot exceeds its byte budget.'
        $writer=[IO.FileStream]::new($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try{
            $buffer=[byte[]]::new(64KB)
            while($remaining -gt 0){$read=$reader.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining));if($read -le 0){throw [IO.IOException]::new('Native event stream shrank during snapshot capture.')};$writer.Write($buffer,0,$read);$remaining-=$read}
            $writer.Flush($true)
        }finally{$writer.Dispose()}
    }finally{$reader.Dispose()}
}
function Read-FrameworkJson([string]$Path) {
    ConvertFrom-FrameworkJson (Read-FrameworkUtf8 $Path)
}
function Read-FrameworkEvents([string]$Path) {
    $text=Read-FrameworkUtf8 $Path
    Require-FrameworkFact ($text.EndsWith("`n") -and $text.Length -gt 1) 'E_EVENTS_TRUNCATED' 'Native JSONL must end with a complete durable record.'
    $lines=$text.Substring(0,$text.Length-1).Split("`n");$events=@()
    foreach($line in $lines){
        Require-FrameworkFact ([Text.Encoding]::UTF8.GetByteCount($line) -le 256KB -and -not [string]::IsNullOrWhiteSpace($line)) 'E_EVENTS_SIZE' 'Invalid or oversized native event.'
        $event=ConvertFrom-FrameworkJson $line -ObjectOnly
        $fields=@('runId','caseId','attemptId','processId','sequence','utc','kind','data')
        Require-FrameworkFact ($event.Count -eq $fields.Count -and @($event.Keys|Where-Object {$_ -cnotin $fields}).Count -eq 0) 'E_EVENT_SCHEMA' 'Native event fields differ from the frozen writer schema.'
        foreach($field in @('runId','caseId','attemptId','utc','kind')){Require-FrameworkFact ($event[$field] -is [string] -and $event[$field].Length -gt 0) 'E_EVENT_SCHEMA' 'Native event strings are required.'}
        Require-FrameworkFact ((Test-FrameworkInteger $event.processId) -and $event.processId -gt 0 -and (Test-FrameworkInteger $event.sequence) -and $event.sequence -gt 0 -and ($null -eq $event.data -or $event.data -is [System.Collections.IDictionary])) 'E_EVENT_SCHEMA' 'Invalid native event PID/sequence/data.'
        Assert-FrameworkScalars $event
        $events+=$event
    }
    return ,$events
}
function Read-FrameworkFirstEvent([string]$Path) {
    Assert-FrameworkNoReparse $Path
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try{
        $bytes=[Collections.Generic.List[byte]]::new()
        while($bytes.Count -le 256KB){$value=$stream.ReadByte();if($value -lt 0){throw [IO.IOException]::new('First native record is not yet complete.')};if($value -eq 10){break};$bytes.Add([byte]$value)}
        Require-FrameworkFact ($bytes.Count -le 256KB) 'E_EVENTS_SIZE' 'First native record exceeds its writer budget.'
        try{$line=[Text.UTF8Encoding]::new($false,$true).GetString($bytes.ToArray())}catch{throw [IO.InvalidDataException]::new('E_UTF8: Invalid native first-record bytes.', $_.Exception)}
        $event=ConvertFrom-FrameworkJson $line -ObjectOnly
        Assert-FrameworkFields $event @('runId','caseId','attemptId','processId','sequence','utc','kind','data')
        Assert-FrameworkScalars $event
        Require-FrameworkFact ($event.kind -ceq 'process.start' -and $event.sequence -eq 1 -and $event.attemptId -cmatch '^[a-f0-9]{32}$') 'E_EVENTS' 'First record must be the native-generated process start.'
        return $event
    }finally{$stream.Dispose()}
}
function Resolve-FrameworkReference([string]$Root,$Reference) {
    Assert-FrameworkFields $Reference @('path','sha256')
    Require-FrameworkFact ($Reference.path -is [string] -and $Reference.path -cnotmatch '(^[/\\]|^[A-Za-z]:|(^|[/\\])\.\.([/\\]|$))' -and $Reference.sha256 -cmatch '^[a-f0-9]{64}$') 'E_REFERENCE' 'Raw evidence reference requires a relative path and exact SHA256.'
    $full=[IO.Path]::GetFullPath((Join-Path $Root $Reference.path));$prefix=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    Require-FrameworkFact ($full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) 'E_REFERENCE' 'Raw evidence escapes its run directory.'
    Assert-FrameworkNoReparse $full
    Require-FrameworkFact ((Get-FrameworkFileHash $full) -ceq $Reference.sha256) 'E_REFERENCE_HASH' "Raw evidence changed: $($Reference.path)"
    return $full
}
function Read-FrameworkReference([string]$Root,$Reference,[switch]$Json) {
    $path=Resolve-FrameworkReference $Root $Reference
    if($Json){return Read-FrameworkJson $path}
    return [IO.File]::ReadAllText($path)
}
function New-FrameworkLaunchCommand {
    param([ValidateSet('Windows64','Android')][string]$Platform,[string]$PlayerPath,[string]$EvidenceRoot,[System.Collections.IDictionary]$Options,[string]$DeviceId,[string]$ApplicationId)
    $allowed=@('runId','caseId','pausePoint','statePurpose','lowSpace','cancelOnTransfer','tok','invalidCredential','autoExit')
    foreach($key in $Options.Keys){Require-FrameworkFact ($key -cin $allowed) 'E_OPTIONS' "Unknown acceptance control: $key"}
    foreach($key in @('runId','caseId')){Require-FrameworkFact ($Options[$key] -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_OPTIONS' 'Bounded safe run/case IDs are required.'}
    if($Options.pausePoint){Require-FrameworkFact ($Options.pausePoint -cin @('download.partial','rename.before','state.replace.before','state.replace.after','trial.persisted','hotentry.executing')) 'E_OPTIONS' 'Unsupported pause boundary.'}
    if($Options.statePurpose){Require-FrameworkFact ($Options.statePurpose -cin @('write','candidate','trial','promote','quarantine','clearCandidate')) 'E_OPTIONS' 'Unsupported state purpose.'}
    Require-FrameworkFact (-not $Options.pausePoint -or -not $Options.pausePoint.StartsWith('state.') -or $Options.statePurpose) 'E_OPTIONS' 'State pauses require the exact write purpose.'
    foreach($key in @('lowSpace','cancelOnTransfer','tok','invalidCredential','autoExit')){if($Options.Contains($key)){Require-FrameworkFact ($Options[$key] -is [bool]) 'E_OPTIONS' 'Acceptance flags must be booleans.'}}
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) 'E_PATH_BUDGET' 'Actual native acceptance root is required.'
    foreach($suffix in @('events.jsonl','checkpoint.json','continue','screen.png','complete.json')){
        $actual=$EvidenceRoot.TrimEnd('/','\')+'/'+$Options.runId+'/'+$Options.caseId+'/'+('0'*32)+'/'+$suffix
        Require-FrameworkFact ([Text.Encoding]::UTF8.GetByteCount($actual) -le 240) 'E_PATH_BUDGET' 'Concrete native attempt evidence exceeds 240 UTF8 bytes.'
    }
    $json=$Options|ConvertTo-Json -Depth 5 -Compress
    Require-FrameworkFact ([Text.Encoding]::UTF8.GetByteCount($json) -le 4096) 'E_OPTIONS' 'Acceptance JSON exceeds its native budget.'
    if($Platform -ceq 'Android'){
        Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($DeviceId) -and $ApplicationId -cmatch '^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$') 'E_DEVICE' 'Explicit Android device and baked application ID are required.'
        # All option strings are bounded safe identifiers/enumerated controls; the JSON contains no apostrophe.
        # adb shell rejoins arguments into a remote shell command, so retain JSON double quotes with one shell literal.
        return @{file=$PlayerPath;arguments=@('-s',$DeviceId,'shell','am','start','-n',($ApplicationId+'/com.unity3d.player.UnityPlayerActivity'),'--es','clientAcceptance',("'"+$json+"'"));acceptanceJson=$json;executed=$false;requiresControllerRelease=$true}
    }
    return @{file=$PlayerPath;arguments=@('-clientAcceptance',$json);acceptanceJson=$json;executed=$false;requiresControllerRelease=$true}
}
function New-FrameworkOutputDirectory([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path);Assert-FrameworkNoReparse $full
    Require-FrameworkFact (-not (Test-Path -LiteralPath $full)) 'E_OUTPUT_EXISTS' 'Choose a fresh output directory; prior evidence is immutable.'
    for($parent=[IO.Path]::GetDirectoryName($full);$parent;$parent=[IO.Path]::GetDirectoryName($parent)){
        foreach($marker in @('archive.json','pending.json','release.json','channel.json')){Require-FrameworkFact (-not (Test-Path -LiteralPath (Join-Path $parent $marker))) 'E_OUTPUT_ARTIFACT' 'Output cannot be inside an immutable archive/release.'}
        if(Test-Path -LiteralPath (Join-Path $parent '.git')){
            foreach($tree in @('Assets','Packages','ProjectSettings','Tool','Tests','docs','.git','.agents','.codex')){$source=(Join-Path $parent $tree).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar;Require-FrameworkFact (-not $full.StartsWith($source,[StringComparison]::OrdinalIgnoreCase)) 'E_OUTPUT_SOURCE' 'Output cannot be inside a source tree.'}
        }
    }
    New-Item -ItemType Directory -Path $full -ErrorAction Stop|Out-Null
    return $full
}
function Write-FrameworkJsonNew([string]$Path,$Value) {
    Assert-FrameworkNoReparse $Path
    $bytes=[Text.Encoding]::UTF8.GetBytes(($Value|ConvertTo-Json -Depth 100)+"`n")
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Write-FrameworkToolEvidence([string]$Directory,[string]$RunId,[string]$TaskId,[string]$ResultPath,[ValidateSet('LegacyNativeF8','EditorGeometry','ManagedInterop')][string]$Scope) {
    Require-FrameworkFact ($RunId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $TaskId -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_AGGREGATE_ID' 'Safe aggregation identities are required.'
    Assert-FrameworkNoReparse $Directory;Assert-FrameworkNoReparse $ResultPath
    Require-FrameworkFact (Test-Path -LiteralPath $Directory -PathType Container) 'E_AGGREGATE_DIRECTORY' 'Aggregation directory must be explicitly prepared.'
    $path=Join-Path $Directory ($TaskId+'.tool-evidence.json')
    Write-FrameworkJsonNew $path @{schemaVersion=1;runId=$RunId;taskId=$TaskId;scope=$Scope;nativeAcceptance=$false;source=@{path=[IO.Path]::GetFullPath($ResultPath);sha256=(Get-FrameworkFileHash $ResultPath)};createdUtc=[DateTime]::UtcNow.ToString('o')}
    return $path
}
function Get-FrameworkInventory([string]$Root) {
    Assert-FrameworkNoReparse $Root
    $map=@{}
    foreach($file in Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction Stop|Where-Object Extension -ne '.meta'){
        Assert-FrameworkNoReparse $file.FullName
        $name=$file.FullName.Substring([IO.Path]::GetFullPath($Root).TrimEnd('\','/').Length+1).Replace('\','/')
        Require-FrameworkFact (-not $map.ContainsKey($name)) 'E_INVENTORY' 'Case-aliased inventory path.'
        $map[$name]=@{length=$file.Length;sha256=(Get-FrameworkFileHash $file.FullName)}
    }
    return $map
}
function Read-FrameworkArchive([string]$PlayerArchive,[string]$BuildResult,[string]$Platform) {
    $archivePath=Join-Path ([IO.Path]::GetFullPath($PlayerArchive)) 'archive.json'
    $archive=Read-FrameworkJson $archivePath;$result=Read-FrameworkJson $BuildResult
    Assert-FrameworkFields $result @('Status','OutputDirectory','ResultFile','PlayerBuildId','ReleaseId','Errors','EvidenceFiles')
    Assert-FrameworkScalars $archive
    Require-FrameworkFact ((Test-FrameworkInteger $archive.schemaVersion) -and $archive.schemaVersion -eq 1 -and (($result.Status -is [string] -and $result.Status -ceq 'Succeeded') -or ((Test-FrameworkInteger $result.Status) -and $result.Status -eq 1)) -and $result.PlayerBuildId -ceq $archive.playerBuildId -and $result.Errors -is [System.Collections.IList] -and $result.Errors.Count -eq 0) 'E_BUILD_RESULT' 'Successful explicit BuildResult and matching immutable Player identity are required.'
    Require-FrameworkFact ([IO.Path]::GetFullPath($result.ResultFile) -ceq [IO.Path]::GetFullPath($BuildResult) -and [IO.Path]::GetFullPath((Join-Path $result.OutputDirectory 'Archive')) -ceq [IO.Path]::GetFullPath($PlayerArchive)) 'E_RECEIPT' 'BuildResult actual output does not own the explicitly supplied archive.'
    $expectedPlatform=if($Platform -ceq 'Windows64'){'StandaloneWindows64'}else{'Android'}
    $expectedArchitecture=if($Platform -ceq 'Windows64'){'x64'}else{'arm64'}
    Require-FrameworkFact ($archive.platform -ceq $expectedPlatform -and $archive.architecture -ceq $expectedArchitecture) 'E_ARCHIVE_PLATFORM' 'Original archive platform/architecture differs.'
    Require-FrameworkFact ($result.EvidenceFiles -is [System.Collections.IList] -and [IO.Path]::GetFullPath($archivePath) -cin @($result.EvidenceFiles|ForEach-Object {[IO.Path]::GetFullPath($_)})) 'E_RECEIPT' 'BuildResult does not list the selected immutable archive.'
    $profile=ConvertFrom-FrameworkJson $archive.buildProfile -ObjectOnly
    Require-FrameworkFact ($profile.development -is [bool] -and $profile.development -eq $false) 'E_RELEASE_BUILD' 'A Release Player is required.'
    foreach($pair in @(@('metadata','Metadata'),@('baseline','Baseline'),@('player','Player'),@('compiled','Compiled'),@('inputs','Inputs'),@('facades','Facades'))){
        $records=@($archive[$pair[0]]);$actual=Get-FrameworkInventory (Join-Path $PlayerArchive $pair[1])
        Require-FrameworkFact ($records.Count -gt 0 -and $records.Count -eq $actual.Count) 'E_ARCHIVE_INVENTORY' "Missing or extra immutable $($pair[0]) files."
        $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($record in $records){Assert-FrameworkFields $record @('path','length','sha256');Require-FrameworkFact ($record.path -is [string] -and (Test-FrameworkInteger $record.length) -and $record.length -ge 0 -and $record.sha256 -cmatch '^[a-f0-9]{64}$' -and $names.Add($record.path) -and $actual.ContainsKey($record.path) -and $actual[$record.path].length -eq $record.length -and $actual[$record.path].sha256 -ceq $record.sha256) 'E_ARCHIVE_HASH' "Immutable archive bytes changed: $($pair[1])/$($record.path)"}
    }
    $native=@{};foreach($record in $archive.player){$native[$record.path]=$record.sha256}
    $metadata=@{};foreach($record in $archive.metadata){$metadata[$record.path]=$record.sha256}
    $identity=@{};foreach($name in @('playerBuildId','platform','architecture','unityVersion','contractVersion','envelopeSha256','aotMetadataSetId')){$identity[$name]=$archive[$name]}
    $publicProfile=Read-FrameworkJson (Join-Path $PlayerArchive 'Inputs/Assets/Tests/Generated/D1/NativeProfile.json')
    Require-FrameworkFact ($publicProfile.packageId -is [string] -and $publicProfile.packageId -cmatch '^[A-Za-z0-9]+(\.[A-Za-z0-9]+)+$') 'E_PROFILE_PACKAGE' 'Original archived native profile package ID is required.'
    $binary=@{}
    if($Platform -ceq 'Windows64'){foreach($name in @('Client.exe','GameAssembly.dll','UnityPlayer.dll')){Require-FrameworkFact ($native.ContainsKey($name)) 'E_PE_FILES' 'Original Windows native binary is missing.';$binary[$name]=Get-FrameworkBinaryEvidence (Join-Path $PlayerArchive ('Player/'+$name)) PE}}
    else{$binary['Client.apk']=Get-FrameworkApkEvidence (Join-Path $PlayerArchive 'Player/Client.apk')}
    return @{archive=$archive;archiveDirectory=[IO.Path]::GetFullPath($PlayerArchive);identity=$identity;archiveNative=$native;nativeBinaryEvidence=$binary;metadataHashes=$metadata;metadataNames=@($metadata.Keys);playerBuildId=$archive.playerBuildId;profilePackageId=$publicProfile.packageId;updateEndpoint=$publicProfile.endpoint;archiveSha256=(Get-FrameworkFileHash $archivePath);buildResultSha256=(Get-FrameworkFileHash $BuildResult);resultFile=[IO.Path]::GetFullPath($BuildResult);outputDirectory=[IO.Path]::GetFullPath($result.OutputDirectory)}
}
function Import-FrameworkNativeCollectionProof([string]$Root,$Reference,$Case,$Archive,[string]$RunId,[string]$Platform) {
    $proof=Read-FrameworkReference $Root $Reference -Json
    Assert-FrameworkFields $proof @('schemaVersion','scope','caseId','source','attempts')
    Require-FrameworkFact ($proof.schemaVersion -eq 1 -and $proof.scope -ceq 'NativeCollectionExport' -and $proof.caseId -ceq $Case.id) 'E_COLLECTION_PROVENANCE' 'Portable collection proof must bind this logical case.'
    $source=Read-FrameworkReference $Root $proof.source -Json
    Require-FrameworkFact ($source.schemaVersion -eq 1 -and $source.evidenceKind -ceq 'NativeCollection' -and $source.nativeAcceptance -is [bool] -and $source.nativeAcceptance -eq $false -and $source.runId -ceq $RunId -and $source.platform -ceq $Platform -and $source.playerBuildId -ceq $Archive.playerBuildId) 'E_COLLECTION_PROVENANCE' 'Original native collector output belongs to a different run, platform or Player.'
    $records=@($source.cases|Where-Object id -ceq $Case.id)
    Require-FrameworkFact ($records.Count -eq 1 -and $records[0].status -ceq 'Collected' -and $records[0].executed -is [bool] -and $records[0].executed -eq $true -and $records[0].attempts.Count -eq $Case.attempts.Count -and $proof.attempts.Count -eq $Case.attempts.Count -and $Case.attempts.Count -gt 0) 'E_COLLECTION_PROVENANCE' 'Every executed native attempt requires an original collected record.'
    $bindings=@()
    for($index=0;$index -lt $Case.attempts.Count;$index++){
        $actual=$Case.attempts[$index];$original=$records[0].attempts[$index];$export=$proof.attempts[$index]
        Assert-FrameworkFields $export @('role','receipt')
        Require-FrameworkFact ($original.role -cin @('primary','recovery') -and $export.role -ceq $original.role -and $original.processId -eq $actual.processId -and $original.attemptId -ceq $actual.attemptId -and $original.terminal.terminal -is [bool] -and $original.terminal.terminal -eq $true -and $original.terminal.processId -eq $actual.processId) 'E_COLLECTION_PROVENANCE' 'Original attempt identity, role or terminal process differs.'
        Require-FrameworkFact ($original.capture.evidenceKind -ceq 'Native' -and $original.capture.schemaAccepted -is [bool] -and $original.capture.schemaAccepted -eq $true -and $original.capture.receipt.sha256 -ceq $export.receipt.sha256) 'E_COLLECTION_PROVENANCE' 'Copied receipt must retain exactly the original native collector bytes.'
        $path=Resolve-FrameworkReference $Root $export.receipt;$descriptor=Read-FrameworkJson $path;$directory=Split-Path -Parent $path
        Require-FrameworkFact ($descriptor.evidenceKind -ceq 'Native' -and $descriptor.schemaAccepted -is [bool] -and $descriptor.schemaAccepted -eq $true -and $descriptor.processId -eq $actual.processId -and $descriptor.attemptId -ceq $actual.attemptId) 'E_COLLECTION_PROVENANCE' 'Native capture descriptor is rejected, synthetic or foreign.'
        $null=Resolve-FrameworkReference $directory $descriptor.events;$process=Read-FrameworkReference $directory $descriptor.process -Json
        Require-FrameworkFact ($descriptor.events.sha256 -ceq $actual.eventsSha256 -and $descriptor.process.sha256 -ceq $actual.processSha256 -and $process.processId -eq $actual.processId -and $process.attemptId -ceq $actual.attemptId -and $process.runId -ceq $RunId -and $process.caseId -ceq $Case.id -and $process.playerBuildId -ceq $Archive.playerBuildId -and $process.terminal -eq $true) 'E_COLLECTION_PROVENANCE' 'Imported event/process bytes differ from the original native capture.'
        $bindings+=@{role=$original.role;processId=$actual.processId;attemptId=$actual.attemptId;receipt=$export.receipt;eventsSha256=$descriptor.events.sha256;processSha256=$descriptor.process.sha256}
    }
    # 导出的路径可以变化；原始采集器、回执、事件和进程文件字节必须一一相同。
    return @{validated=$true;caseId=$Case.id;source=$proof.source;proof=$Reference;attempts=$bindings}
}
function Import-FrameworkEvidence([string]$Path,$Archive,[string]$Platform,[string]$DeviceId) {
    $bundle=Read-FrameworkJson $Path;$root=Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    Assert-FrameworkFields $bundle @('schemaVersion','evidenceKind','runId','platform','archiveSha256','buildResultSha256','buildReceipt','nativeBefore','nativeAfter','releases','cases') @('deviceId','environment','executionGate')
    Assert-FrameworkScalars $bundle
    Require-FrameworkFact ($bundle.schemaVersion -eq 1 -and $bundle.evidenceKind -in @('Native','SyntheticUnit') -and $bundle.platform -ceq $Platform -and ($Platform -ne 'Android' -or $bundle.deviceId -ceq $DeviceId)) 'E_BUNDLE' 'Evidence platform/device/schema differs from the explicit invocation.'
    Require-FrameworkFact ($bundle.archiveSha256 -ceq $Archive.archiveSha256 -and $bundle.buildResultSha256 -ceq $Archive.buildResultSha256) 'E_BUNDLE_ARCHIVE' 'Evidence does not bind to the exact final archive and actual BuildResult.'
    $receipt=Read-FrameworkReference $root $bundle.buildReceipt -Json
    Assert-FrameworkFields $receipt @('Status','OutputDirectory','ResultFile')
    Require-FrameworkFact ((($receipt.Status -is [string] -and $receipt.Status -ceq 'Succeeded') -or ((Test-FrameworkInteger $receipt.Status) -and $receipt.Status -eq 1)) -and [IO.Path]::GetFullPath($receipt.ResultFile) -ceq $Archive.resultFile -and [IO.Path]::GetFullPath($receipt.OutputDirectory) -ceq $Archive.outputDirectory) 'E_RECEIPT' 'Producer receipt differs from the explicit successful BuildResult.'
    $evidence=@{schemaVersion=1;runId=$bundle.runId;platform=$Platform;deviceId=$DeviceId;evidenceKind=$bundle.evidenceKind;playerBuildId=$Archive.playerBuildId;identity=$Archive.identity;profilePackageId=$Archive.profilePackageId;archiveNative=$Archive.archiveNative;metadataNames=$Archive.metadataNames;metadataHashes=$Archive.metadataHashes;cases=@();releases=@{};sourceBundle=@{path=[IO.Path]::GetFullPath($Path);sha256=(Get-FrameworkFileHash $Path)}}
    $evidence.nativeBinaryEvidence=$Archive.nativeBinaryEvidence
    $evidence.updateEndpoint=$Archive.updateEndpoint
    $evidence.sourceArchive=@{path=$Archive.archiveDirectory;sha256=$Archive.archiveSha256;buildResult=$Archive.resultFile;buildResultSha256=$Archive.buildResultSha256}
    foreach($name in @('nativeBefore','nativeAfter')){$evidence[$name]=Read-FrameworkReference $root $bundle[$name] -Json}
    if($bundle.evidenceKind -ceq 'Native'){
        $gatePath=Resolve-FrameworkReference $root $bundle.executionGate
        Require-FrameworkFact ($null -ne (Get-Command Read-FrameworkExecutionGate -ErrorAction SilentlyContinue)) 'E_NATIVE_GATE' 'Native D1 raw-proof validator must be loaded.'
        $gate=Read-FrameworkExecutionGate $gatePath $Archive $Platform $root
        $evidence.executionGate=$gate
    }
    if($Platform -ceq 'Android'){$evidence.environment=Read-FrameworkReference $root $bundle.environment -Json}
    foreach($id in $bundle.releases.Keys){
        Require-FrameworkFact ($id -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_RELEASE_ID' 'Unsafe release identity.'
        $release=$bundle.releases[$id]
        $manifest=Read-FrameworkReference $root $release.manifest -Json
        $dll=Resolve-FrameworkReference $root $release.dll;$protocol=Resolve-FrameworkReference $root $release.protocolDll;$table=Resolve-FrameworkReference $root $release.table
        Require-FrameworkFact ($manifest.releaseId -ceq $id) 'E_RELEASE_INPUT' 'Release input identity differs.'
        foreach($pair in @(@('Hotfix/Client.Game.dll',$release.dll.sha256,$dll),@('Hotfix/Client.Game.Protocol.dll',$release.protocolDll.sha256,$protocol),@('Resources/GameFrameworkVersion.dat',$release.table.sha256,$table))){Require-FrameworkFact (@($manifest.files|Where-Object {$_.path -ceq $pair[0] -and $_.sha256 -ceq $pair[1] -and (Test-FrameworkInteger $_.length) -and $_.length -eq (Get-Item -LiteralPath $pair[2]).Length}).Count -eq 1) 'E_RELEASE_INPUT' 'Producer file bytes/length differ from their manifest inventory.'}
        $files=@{};$lengths=@{};foreach($file in $manifest.files){Require-FrameworkFact (-not $files.ContainsKey($file.path) -and (Test-FrameworkInteger $file.length) -and $file.length -ge 0) 'E_RELEASE_INPUT' 'Duplicate manifest file or invalid exact length.';$files[$file.path]=$file.sha256;$lengths[$file.path]=$file.length}
        $evidence.releases[$id]=@{manifestSha256=$release.manifest.sha256;dllSha256=$release.dll.sha256;protocolDllSha256=$release.protocolDll.sha256;tableSha256=$release.table.sha256;files=$files;fileLengths=$lengths;sourcePaths=@($release.manifest.path,$release.dll.path,$release.protocolDll.path,$release.table.path)}
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($source in $bundle.cases){
        Assert-FrameworkFields $source @('id','executed','attempts') @('knownGood','previousGood','candidate','fault','preservation','operator','kill','install','server','transfer','preparedCandidate','collection')
        $case=@{id=$source.id;executed=($source.executed -eq $true);knownGood=$source.knownGood;previousGood=$source.previousGood;candidate=$source.candidate;attempts=@()}
        if($case.executed){
            if($source.transfer){Assert-FrameworkFields $source.transfer @('holding','abort');$case.transfer=@{holding=(Read-FrameworkReference $root $source.transfer.holding -Json);abort=(Read-FrameworkReference $root $source.transfer.abort -Json)}}
            if($source.preparedCandidate){$case.preparedCandidate=Read-FrameworkReference $root $source.preparedCandidate -Json}
            foreach($name in @('fault','preservation','operator','kill','install','server')){if($source[$name]){$case[$name]=Read-FrameworkReference $root $source[$name] -Json}}
            foreach($name in @('fault','install','server')){
                if($case[$name]){
                    Require-FrameworkFact ($case[$name].files -is [System.Collections.IList] -and $case[$name].files.Count -gt 0) 'E_CAPTURE_FILES' "$name must retain its exact original file references."
                    $paths=@();foreach($file in $case[$name].files){$null=Resolve-FrameworkReference $root $file;$paths+=$file.path}
                    if($name -ceq 'fault'){$case.fault.inputPaths=$paths}else{$case[$name].evidencePaths=$paths}
                }
            }
            if($source.id -ceq 'throw-on-start'){
                $approved=Assert-FrameworkThrowFixture (Join-Path $PSScriptRoot '../../Tests/Fixtures/Delivery')
                Require-FrameworkFact ($case.fault.fixtureContractSha256 -ceq $approved.contractSha256) 'E_THROW_PROVENANCE' 'Throw source contract must match the approved Tests-only fixture.'
            }
            foreach($sourceAttempt in $source.attempts){
                $eventPath=Resolve-FrameworkReference $root $sourceAttempt.events
                Require-FrameworkFact ($seen.Add($eventPath)) 'E_ATTEMPT_REUSED' 'Raw attempt file is reused by another case/attempt.'
                Require-FrameworkFact ((Get-Item -LiteralPath $eventPath).Length -le 32MB) 'E_EVENTS_SIZE' 'Native event stream exceeds its budget.'
                $events=Read-FrameworkEvents $eventPath
                Require-FrameworkFact ($events.Count -gt 0) 'E_EVENTS' 'Native event stream is empty.'
                $process=Read-FrameworkReference $root $sourceAttempt.process -Json
                $attempt=@{caseId=$case.id;attemptId=$events[0].attemptId;processId=$events[0].processId;observedProcessId=$process.processId;process=$process;events=$events;evidencePaths=@($sourceAttempt.events.path,$sourceAttempt.process.path);eventsSha256=$sourceAttempt.events.sha256;processSha256=$sourceAttempt.process.sha256}
                if($sourceAttempt.slotInventory){$attempt.slotInventory=Read-FrameworkReference $root $sourceAttempt.slotInventory -Json;$attempt.evidencePaths+=$sourceAttempt.slotInventory.path}
                if($sourceAttempt.checkpoint){$attempt.checkpoint=Read-FrameworkReference $root $sourceAttempt.checkpoint -Json;$attempt.evidencePaths+=$sourceAttempt.checkpoint.path}
                $case.attempts+=$attempt
            }
            if($case.operator){foreach($shot in $case.operator.screenshots){$null=Resolve-FrameworkReference $root $shot}}
            if($bundle.evidenceKind -ceq 'Native'){
                Require-FrameworkFact ($null -ne $source.collection) 'E_COLLECTION_PROVENANCE' 'Executed native cases require the original portable collector proof.'
                $case.collectionProvenance=Import-FrameworkNativeCollectionProof $root $source.collection $case $Archive $bundle.runId $Platform
            }
        }
        $evidence.cases+=$case
    }
    return $evidence
}
function Read-FrameworkAcceptanceSource($Evidence) {
    Require-FrameworkFact ($Evidence.evidenceKind -ceq 'Native' -and $Evidence.sourceBundle -is [Collections.IDictionary] -and $Evidence.sourceArchive -is [Collections.IDictionary]) 'E_NATIVE_SOURCE' 'Native acceptance requires the imported original file bundle and archive, not approval flags.'
    $source=$Evidence.sourceBundle;$baseline=$Evidence.sourceArchive
    Require-FrameworkFact ([IO.Path]::IsPathFullyQualified([string]$source.path) -and [IO.Path]::IsPathFullyQualified([string]$baseline.path) -and [IO.Path]::IsPathFullyQualified([string]$baseline.buildResult)) 'E_NATIVE_SOURCE' 'Explicit canonical source paths are required.'
    Require-FrameworkFact ((Get-FrameworkFileHash $source.path) -ceq $source.sha256 -and (Get-FrameworkFileHash (Join-Path $baseline.path 'archive.json')) -ceq $baseline.sha256 -and (Get-FrameworkFileHash $baseline.buildResult) -ceq $baseline.buildResultSha256) 'E_NATIVE_SOURCE' 'Imported bundle, original archive or BuildResult changed before acceptance.'
    # 最终汇总重新读取原始文件；调用方修改内存中的状态或用例不能获得验收资格。
    $archive=Read-FrameworkArchive $baseline.path $baseline.buildResult $Evidence.platform
    $canonical=Import-FrameworkEvidence $source.path $archive $Evidence.platform $Evidence.deviceId
    Require-FrameworkFact ($canonical.evidenceKind -ceq 'Native' -and $canonical.runId -ceq $Evidence.runId -and $canonical.playerBuildId -ceq $Evidence.playerBuildId) 'E_NATIVE_SOURCE' 'A synthetic, foreign or stale source cannot be promoted to native acceptance.'
    return $canonical
}
function Assert-FrameworkThrowFixture([string]$DeliveryRoot) {
    $contractPath=Join-Path $DeliveryRoot 'throw-provenance.json';$contract=Read-FrameworkJson $contractPath
    $original=Read-FrameworkJson (Join-Path $DeliveryRoot 'provenance.json')
    Require-FrameworkFact ($contract.schemaVersion -eq 1 -and $contract.sharedSourceCommit -ceq $original.sharedSourceCommit -and $contract.markerCode -ceq 'CLIENT_DELIVERY_THROW' -and $contract.markerMessage -ceq 'after login first frame; before ReportReady' -and $contract.exceptionLine -ceq 'System.InvalidOperationException: D2 Delivery ThrowOnStart' -and $contract.origin -ceq 'Client.Game.DeliveryVersion.Run') 'E_THROW_CONTRACT' 'Throw fixture contract differs from the approved Delivery-path cause.'
    $expected=@('V2/Assets/Scripts/Game/DeliveryVersion.cs','V2/Assets/Scripts/Game/Protocol/Delivery/Added.cs','ThrowOnStart/Assets/Scripts/Game/DeliveryVersion.cs','ThrowOnStart/Assets/Scripts/Game/Protocol/Delivery/Added.cs')
    Require-FrameworkFact ($contract.files.Count -eq 4 -and @($contract.files.path|Sort-Object -Unique).Count -eq 4) 'E_THROW_FILES' 'Exact original and throw fixture file inventory is required.'
    foreach($file in $contract.files){Require-FrameworkFact ($file.path -cin $expected) 'E_THROW_FILES' 'Unexpected throw overlay file.';$null=Resolve-FrameworkReference $DeliveryRoot $file}
    foreach($path in $expected|Where-Object {$_ -like 'V2/*'}){
        $baseline=@($original.files|Where-Object path -CEQ $path)
        Require-FrameworkFact ($baseline.Count -eq 1 -and (Get-FrameworkFileHash (Join-Path $DeliveryRoot $path)) -ceq $baseline[0].sha256) 'E_THROW_BASE' 'Original V2 probe/generated bytes changed.'
    }
    Require-FrameworkFact ((Get-FrameworkFileHash (Join-Path $DeliveryRoot $expected[1])) -ceq (Get-FrameworkFileHash (Join-Path $DeliveryRoot $expected[3]))) 'E_THROW_GENERATED' 'Throw overlay must reuse byte-identical shared V2 generated messages.'
    $baseline=Read-FrameworkUtf8 (Join-Path $DeliveryRoot $expected[0]);$throw=Read-FrameworkUtf8 (Join-Path $DeliveryRoot $expected[2])
    $line='            log?.Write(ClientLogLevel.Info,"CLIENT_CODE_VERSION","v2");log?.Write(ClientLogLevel.Info,"CLIENT_PARSER_EXECUTED","AddedMessage list,map,unknown-fields,uint64,hot-generics,empty-registry");'
    $addition="`n"+'            log?.Write(ClientLogLevel.Error,"CLIENT_DELIVERY_THROW","after login first frame; before ReportReady");'+"`n"+'            throw new InvalidOperationException("D2 Delivery ThrowOnStart");'
    Require-FrameworkFact ($baseline.Contains($line) -and $throw -ceq $baseline.Replace($line,$line+$addition)) 'E_THROW_PROBES' 'Only the approved marker and exception may follow the unchanged V2 probes.'
    return @{passed=$true;nativeAcceptance=$false;generatedByteIdentical=$true;unityCompilation='NotRun';contractSha256=(Get-FrameworkFileHash $contractPath);files=$contract.files}
}
