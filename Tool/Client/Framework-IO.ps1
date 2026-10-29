#requires -Version 7.0
# File-only preparation/import helpers. Command construction deliberately never starts a process.
function Get-FrameworkFileHash([string]$Path) {(Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()}
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
function ConvertFrom-FrameworkJson([string]$Text,[switch]$ObjectOnly) {
    try{$document=[System.Text.Json.JsonDocument]::Parse($Text)}catch{throw [IO.InvalidDataException]::new('E_JSON: Invalid strict JSON.', $_.Exception)}
    function CheckElement($Element) {
        if($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object){
            $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($property in $Element.EnumerateObject()){Require-FrameworkFact ($names.Add($property.Name)) 'E_JSON_DUPLICATE' 'Duplicate or case-aliased JSON key.';CheckElement $property.Value}
        }elseif($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array){foreach($element in $Element.EnumerateArray()){CheckElement $element}}
    }
    function ConvertElement($Element) {
        switch($Element.ValueKind.ToString()) {
            'Object' {$map=@{};foreach($property in $Element.EnumerateObject()){$map[$property.Name]=ConvertElement $property.Value};return $map}
            'Array' {$items=@();foreach($child in $Element.EnumerateArray()){$items+=,(ConvertElement $child)};return ,$items}
            'String' {return $Element.GetString()}
            'True' {return $true}
            'False' {return $false}
            'Null' {return $null}
            'Number' {$signed=0L;$unsigned=0UL;if($Element.TryGetInt64([ref]$signed)){return $signed};if($Element.TryGetUInt64([ref]$unsigned)){return $unsigned};return $Element.GetDouble()}
            default {throw [IO.InvalidDataException]::new('E_JSON_VALUE: Unsupported JSON value.')}
        }
    }
    try{
        if($ObjectOnly){Require-FrameworkFact ($document.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) 'E_JSON_ROOT' 'A JSON object is required.'}
        CheckElement $document.RootElement
        # Preserve JSON scalar types, including UTC strings; PowerShell's JSON conversion can coerce dates.
        return ConvertElement $document.RootElement
    }finally{$document.Dispose()}
}
function Read-FrameworkUtf8([string]$Path,[long]$MaximumBytes=32MB) {
    Assert-FrameworkNoReparse $Path
    Require-FrameworkFact ((Get-Item -LiteralPath $Path -ErrorAction Stop).Length -le $MaximumBytes) 'E_JSON_SIZE' 'Evidence exceeds its byte budget.'
    try{return [Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($Path))}catch{throw [IO.InvalidDataException]::new('E_UTF8: Evidence is not valid UTF8.', $_.Exception)}
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
    return @{archive=$archive;identity=$identity;archiveNative=$native;metadataHashes=$metadata;metadataNames=@($metadata.Keys);playerBuildId=$archive.playerBuildId;archiveSha256=(Get-FrameworkFileHash $archivePath);buildResultSha256=(Get-FrameworkFileHash $BuildResult);resultFile=[IO.Path]::GetFullPath($BuildResult);outputDirectory=[IO.Path]::GetFullPath($result.OutputDirectory)}
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
    $evidence=@{schemaVersion=1;runId=$bundle.runId;platform=$Platform;deviceId=$DeviceId;evidenceKind=$bundle.evidenceKind;playerBuildId=$Archive.playerBuildId;identity=$Archive.identity;archiveNative=$Archive.archiveNative;metadataNames=$Archive.metadataNames;metadataHashes=$Archive.metadataHashes;cases=@();releases=@{};sourceBundle=@{path=[IO.Path]::GetFullPath($Path);sha256=(Get-FrameworkFileHash $Path)}}
    foreach($name in @('nativeBefore','nativeAfter')){$evidence[$name]=Read-FrameworkReference $root $bundle[$name] -Json}
    if($bundle.evidenceKind -ceq 'Native'){
        $gate=Read-FrameworkReference $root $bundle.executionGate -Json
        Assert-FrameworkFields $gate @('status','finalD1ArchivesApproved','platform','playerBuildId','archiveSha256','producerCommit')
        Assert-FrameworkScalars $gate
        Require-FrameworkFact ($gate.status -ceq 'Released' -and $gate.finalD1ArchivesApproved -eq $true -and $gate.platform -ceq $Platform -and $gate.playerBuildId -ceq $Archive.playerBuildId -and $gate.archiveSha256 -ceq $Archive.archiveSha256 -and $gate.producerCommit -cmatch '^[a-f0-9]{40}$') 'E_NATIVE_GATE' 'Controller execution release and final D1 archive approval are pending.'
        $evidence.executionGate=$gate
    }
    if($Platform -ceq 'Android'){$evidence.environment=Read-FrameworkReference $root $bundle.environment -Json}
    foreach($id in $bundle.releases.Keys){
        Require-FrameworkFact ($id -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_RELEASE_ID' 'Unsafe release identity.'
        $release=$bundle.releases[$id]
        $manifest=Read-FrameworkReference $root $release.manifest -Json
        $dll=Resolve-FrameworkReference $root $release.dll;$table=Resolve-FrameworkReference $root $release.table
        Require-FrameworkFact ($manifest.releaseId -ceq $id) 'E_RELEASE_INPUT' 'Release input identity differs.'
        foreach($pair in @(@('Hotfix/Client.Game.dll',$release.dll.sha256,$dll),@('Resources/GameFrameworkVersion.dat',$release.table.sha256,$table))){Require-FrameworkFact (@($manifest.files|Where-Object {$_.path -ceq $pair[0] -and $_.sha256 -ceq $pair[1] -and (Test-FrameworkInteger $_.length) -and $_.length -eq (Get-Item -LiteralPath $pair[2]).Length}).Count -eq 1) 'E_RELEASE_INPUT' 'Producer file bytes/length differ from their manifest inventory.'}
        $files=@{};foreach($file in $manifest.files){Require-FrameworkFact (-not $files.ContainsKey($file.path)) 'E_RELEASE_INPUT' 'Duplicate manifest file.';$files[$file.path]=$file.sha256}
        $evidence.releases[$id]=@{manifestSha256=$release.manifest.sha256;dllSha256=$release.dll.sha256;tableSha256=$release.table.sha256;files=$files;sourcePaths=@($release.manifest.path,$release.dll.path,$release.table.path)}
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($source in $bundle.cases){
        Assert-FrameworkFields $source @('id','executed','attempts') @('knownGood','previousGood','candidate','fault','preservation','operator','kill','install','server')
        $case=@{id=$source.id;executed=($source.executed -eq $true);knownGood=$source.knownGood;previousGood=$source.previousGood;candidate=$source.candidate;attempts=@()}
        if($case.executed){
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
                $attempt=@{caseId=$case.id;attemptId=$events[0].attemptId;processId=$events[0].processId;observedProcessId=$process.processId;process=$process;events=$events;evidencePaths=@($sourceAttempt.events.path,$sourceAttempt.process.path);eventsSha256=$sourceAttempt.events.sha256}
                if($sourceAttempt.slotInventory){$attempt.slotInventory=Read-FrameworkReference $root $sourceAttempt.slotInventory -Json;$attempt.evidencePaths+=$sourceAttempt.slotInventory.path}
                if($sourceAttempt.checkpoint){$attempt.checkpoint=Read-FrameworkReference $root $sourceAttempt.checkpoint -Json;$attempt.evidencePaths+=$sourceAttempt.checkpoint.path}
                $case.attempts+=$attempt
            }
            if($case.operator){foreach($shot in $case.operator.screenshots){$null=Resolve-FrameworkReference $root $shot}}
        }
        $evidence.cases+=$case
    }
    return $evidence
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
