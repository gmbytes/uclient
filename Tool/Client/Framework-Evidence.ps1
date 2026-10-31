#requires -Version 7.0
# Pure evidence evaluation. This file does not launch processes, mutate native state, or contact a device.
function Get-FrameworkMatrix {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../Tests/Fixtures/Releases/FaultMatrix.json') -Raw | ConvertFrom-Json -AsHashtable
}
function Require-FrameworkFact($Condition,[string]$Code,[string]$Message) {
    if(-not $Condition){throw [IO.InvalidDataException]::new($Code+': '+$Message)}
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
function Test-FrameworkInteger($Value) { $Value -is [byte] -or $Value -is [int] -or $Value -is [long] -or $Value -is [uint32] -or $Value -is [uint64] }
function Assert-FrameworkScalars($Value,[string]$Path='evidence') {
    if($Value -is [System.Collections.IDictionary]){
        foreach($key in $Value.Keys){
            $item=$Value[$key];$at=$Path+'.'+$key
            if($key -cin @('executed','terminal','ready','restartRequired','Succeeded','succeeded','authenticated','clean','active','interactable','focused','keyboardVisible','paused','automatic','finalD1ArchivesApproved','development','completed')){Require-FrameworkFact ($item -is [bool]) 'E_SCALAR' "$at must be a JSON boolean."}
            if($null -ne $item -and $key -cin @('processId','observedProcessId','sequence','frame','firstSequence','lastSequence','Epoch','epoch','requests','request','requestId','connectionEpoch','serverEpoch','metadataCount','resourceAttempts','stableFrames','pointerBits','bytes','expectedBytes','serial','count','installCount','caret','selectionAnchor','selectionFocus')){Require-FrameworkFact (Test-FrameworkInteger $item) 'E_SCALAR' "$at must be an integer."}
            if($null -ne $item -and $key -cin @('x','y','width','height','left','right','top','bottom','realtime')){Require-FrameworkFact ($item -is [ValueType] -and $item -isnot [bool] -and [double]::IsFinite([double]$item)) 'E_SCALAR' "$at must be a finite number."}
            Assert-FrameworkScalars $item $at
        }
    }elseif($Value -is [System.Collections.IList]){for($i=0;$i -lt $Value.Count;$i++){Assert-FrameworkScalars $Value[$i] ($Path+'['+$i+']')}}
}
function Assert-FrameworkHashMap($Expected,$Actual,[string]$Label) {
    Require-FrameworkFact ($Expected -is [System.Collections.IDictionary] -and $Expected.Count -gt 0 -and $Actual -is [System.Collections.IDictionary] -and $Expected.Count -eq $Actual.Count) 'E_HASH' "$Label inventory is missing or incomplete."
    foreach($key in $Expected.Keys){Require-FrameworkFact ($Expected[$key] -cmatch '^[a-f0-9]{64}$' -and $Actual[$key] -ceq $Expected[$key]) 'E_HASH' "$Label changed: $key"}
}
function Assert-FrameworkContext($Evidence) {
    Assert-FrameworkScalars $Evidence
    Require-FrameworkFact ($Evidence.runId -cmatch '^[A-Za-z0-9_-]{1,40}$') 'E_RUN_ID' 'Unsafe or missing run identity.'
    Require-FrameworkFact ($Evidence.platform -in @('Windows64','Android')) 'E_PLATFORM' 'Explicit native platform is required.'
    Require-FrameworkFact ($Evidence.platform -ne 'Android' -or -not [string]::IsNullOrWhiteSpace($Evidence.deviceId)) 'E_DEVICE' 'Explicit selected device is required.'
    Require-FrameworkFact ($Evidence.evidenceKind -in @('SyntheticUnit','Native')) 'E_SCOPE' 'Evidence scope is missing.'
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($Evidence.playerBuildId)) 'E_IDENTITY' 'Player identity is missing.'
    $platform=if($Evidence.platform -ceq 'Windows64'){'StandaloneWindows64'}else{'Android'}
    $architecture=if($Evidence.platform -ceq 'Windows64'){'x64'}else{'arm64'}
    Require-FrameworkFact ($Evidence.identity.Platform -ceq $platform -and $Evidence.identity.Architecture -ceq $architecture -and $Evidence.identity.PlayerBuildId -ceq $Evidence.playerBuildId) 'E_IDENTITY' 'Original Player platform/architecture differs from the run.'
    Assert-FrameworkHashMap $Evidence.archiveNative $Evidence.nativeBefore 'Native before'
    Assert-FrameworkHashMap $Evidence.archiveNative $Evidence.nativeAfter 'Native after'
    if($Evidence.evidenceKind -ceq 'Native'){
        $proofs=$Evidence.nativeBinaryEvidence
        Require-FrameworkFact ($proofs -is [System.Collections.IDictionary] -and $proofs.Count -gt 0) 'E_BINARY_EVIDENCE' 'Actual original PE/ELF machine proof is required independently of runtime pointer width.'
        if($Evidence.platform -ceq 'Windows64'){
            foreach($name in @('Client.exe','GameAssembly.dll','UnityPlayer.dll')){$proof=$proofs[$name];Require-FrameworkFact ($proof.format -ceq 'PE' -and $proof.machine -eq 34404 -and $proof.wordBits -eq 64 -and $proof.sha256 -ceq $Evidence.archiveNative[$name]) 'E_BINARY_EVIDENCE' 'Original Windows AMD64 PE proof is missing or unbound.'}
        }else{
            $apk=$proofs['Client.apk'];Require-FrameworkFact ($apk.format -ceq 'APK' -and $apk.sha256 -ceq $Evidence.archiveNative['Client.apk'] -and $apk.libraries.Count -gt 0) 'E_BINARY_EVIDENCE' 'Original APK/ELF proof is missing or unbound.'
            $libraryHashes=@{};foreach($name in $apk.libraries.Keys){$proof=$apk.libraries[$name];Require-FrameworkFact ($name.StartsWith('lib/arm64-v8a/',[StringComparison]::Ordinal) -and $proof.format -ceq 'ELF' -and $proof.machine -eq 183 -and $proof.wordBits -eq 64) 'E_BINARY_EVIDENCE' 'APK contains missing or wrong actual AArch64 machine proof.';$libraryHashes[$name]=$proof.sha256}
            Assert-FrameworkHashMap $libraryHashes $Evidence.environment.archiveNativeLibraries 'Original APK native ELF inventory'
        }
    }
    Require-FrameworkFact (@($Evidence.metadataNames).Count -gt 0 -and @($Evidence.metadataNames|Where-Object {$_ -like 'Client.Game*'}).Count -eq 0) 'E_METADATA' 'Original AOT metadata is absent or contains hot assemblies.'
    foreach($key in @('manifestSha256','dllSha256','protocolDllSha256','tableSha256')){
        Require-FrameworkFact ($Evidence.releases.v1[$key] -cmatch '^[a-f0-9]{64}$' -and $Evidence.releases.v2[$key] -cmatch '^[a-f0-9]{64}$' -and $Evidence.releases.v1[$key] -cne $Evidence.releases.v2[$key]) 'E_RELEASE_INPUT' "V1/V2 $key must identify distinct producer bytes."
    }
    if($Evidence.platform -ceq 'Android'){
        $environment=$Evidence.environment
        Require-FrameworkFact ($environment.deviceId -ceq $Evidence.deviceId -and $environment.deviceState -ceq 'device' -and -not [string]::IsNullOrWhiteSpace($environment.primaryAbi) -and $environment.installedPrimaryAbi -ceq 'arm64-v8a' -and -not [string]::IsNullOrWhiteSpace($environment.translation) -and $environment.kind -ceq 'LDPlayer' -and $environment.physicalArm -ceq 'NotRun') 'E_ANDROID_ENVIRONMENT' 'Selected LDPlayer ABI/translation and physical-ARM limitation must be recorded.'
        Require-FrameworkFact ($Evidence.archiveNative['Client.apk'] -cmatch '^[a-f0-9]{64}$' -and $environment.installedApkBeforeSha256 -ceq $Evidence.archiveNative['Client.apk'] -and $environment.installedApkAfterSha256 -ceq $Evidence.archiveNative['Client.apk'] -and $environment.installCount -eq 1 -and $environment.apkCertificateSha256 -cmatch '^[a-f0-9]{64}$') 'E_ANDROID_PACKAGE' 'Installed APK/certificate must remain the original package without reinstall during the matrix.'
        Assert-FrameworkHashMap $environment.archiveNativeLibraries $environment.installedNativeLibraries 'Android ARM64 native libraries'
    }
}
function Get-FrameworkEvents($Attempt,[string]$Kind) { @($Attempt.events | Where-Object kind -CEQ $Kind) }
function Assert-FrameworkThrow($Case,$Evidence) {
    Assert-FrameworkScalars $Case
    Require-FrameworkFact ($Case.attempts.Count -eq 2 -and $Case.candidate -cne $Case.knownGood -and $Case.fault.kind -ceq 'ThrowOnStart') 'E_THROW' 'A distinct throw candidate and next-process recovery are required.'
    $first=$Case.attempts[0];$next=$Case.attempts[1]
    Require-FrameworkFact ($first.processId -ne $next.processId -and $first.attemptId -cne $next.attemptId) 'E_THROW_RECOVERY' 'Throw recovery requires a complete new native process/attempt.'
    $results=@(Get-FrameworkEvents $first 'launch.result');Require-FrameworkFact ($results.Count -eq 1) 'E_THROW_RESULT' 'Actual throwing launch result is required.'
    $result=$results[0].data;$selection=$result.selection
    Require-FrameworkFact ($selection.ReleaseId -ceq $Case.candidate -and $selection.Epoch -gt 0 -and $selection.ManifestSha256 -ceq $Evidence.releases[$Case.candidate].manifestSha256 -and $result.ready -eq $false -and $result.restartRequired -eq $true -and $result.state.KnownGood -ceq $Case.knownGood -and $Case.candidate -cin $result.state.BadReleaseIds) 'E_THROW_SELECTION' 'Throw must quarantine the selected candidate without promotion.'
    $contract=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../Tests/Fixtures/Delivery/throw-provenance.json') -Raw|ConvertFrom-Json -AsHashtable
    $markers=@(Get-FrameworkEvents $first 'hot.log'|Where-Object {$_.data.code -ceq $contract.markerCode -and $_.data.message -ceq $contract.markerMessage})
    $views=@(Get-FrameworkEvents $first 'hot.log'|Where-Object {$_.data.code -ceq 'CLIENT_VIEW'})
    Require-FrameworkFact ($markers.Count -eq 1 -and $views.Count -eq 1 -and $views[0].sequence -lt $markers[0].sequence -and $markers[0].sequence -lt $results[0].sequence) 'E_THROW_MARKER' 'Actual Delivery marker after the real View and before failure is required.'
    $view=ConvertFrom-FrameworkJson $views[0].data.message -ObjectOnly
    Require-FrameworkFact ((Test-FrameworkObjectId $view.view) -and (Test-FrameworkObjectId $view.presenter) -and $view.version -ceq ('Framework '+$Case.candidate)) 'E_THROW_RESOURCE' 'Throw must execute after the actual candidate View/resource initialization.'
    $probes=@(Get-FrameworkEvents $first 'hot.log'|Where-Object {$_.data.code -ceq 'CLIENT_PARSER_EXECUTED' -and $_.data.message -ceq 'AddedMessage list,map,unknown-fields,uint64,hot-generics,empty-registry'})
    Require-FrameworkFact ($probes.Count -eq 1 -and $views[0].sequence -lt $probes[0].sequence -and $probes[0].sequence -lt $markers[0].sequence) 'E_THROW_PROBES' 'Unchanged shared V2 probes must execute before the intentional Delivery throw.'
    $exact='^'+[regex]::Escape($contract.exceptionLine)+'\r?\n'
    $failures=@($result.failures|Where-Object {$_.ReleaseId -ceq $selection.ReleaseId -and $_.Epoch -eq $selection.Epoch -and $_.phase -ceq 'Hotfix' -and $_.Code -ceq 'HotfixInitializationFailed' -and $_.error -cmatch $exact -and $_.error -cmatch ('(?m)^\s*at '+[regex]::Escape($contract.origin)+'\s*\(')})
    Require-FrameworkFact ($failures.Count -eq 1 -and @(Get-FrameworkEvents $first 'release.phase'|Where-Object {$_.data.phase -in @('LoginReady','Ready')}).Count -eq 0) 'E_THROW_CAUSE' 'Exact Delivery exception/cause/epoch is required; no Ready is permitted.'
    Require-FrameworkFact ($result.resourceAttempts -eq 1 -and $result.metadataCount -eq $Evidence.metadataNames.Count -and @($result.hotAssemblies).Count -eq 2) 'E_THROW_LOAD' 'Throw must follow one actual resource/metadata/hot assembly load.'
    $inventory=$first.slotInventory
    Require-FrameworkFact ($inventory.releaseId -ceq $selection.ReleaseId -and $inventory.root -ceq $selection.Root -and $inventory.manifestSha256 -ceq $selection.ManifestSha256 -and $inventory.dllSha256 -ceq $Evidence.releases[$Case.candidate].dllSha256 -and $inventory.protocolDllSha256 -ceq $Evidence.releases[$Case.candidate].protocolDllSha256 -and $inventory.protocolDllSha256 -ceq $Evidence.releases.v2.protocolDllSha256 -and $inventory.tableSha256 -ceq $Evidence.releases[$Case.candidate].tableSha256) 'E_THROW_BYTES' 'Throwing Game DLL, unchanged V2 Protocol DLL, table and root must bind to the candidate manifest.'
    $metadata=@(Get-FrameworkEvents $first 'metadata')
    Require-FrameworkFact ($metadata.Count -eq $Evidence.metadataNames.Count) 'E_THROW_METADATA' 'Every original metadata load must actually be observed before the intentional throw.'
    foreach($name in $Evidence.metadataNames){Require-FrameworkFact (@($metadata|Where-Object {$_.data.message -ceq "F6_METADATA $($Evidence.playerBuildId) $name OK" -and $_.data.selection.ManifestSha256 -ceq $selection.ManifestSha256 -and $_.sequence -lt $markers[0].sequence}).Count -eq 1 -and $inventory.metadata[$name] -ceq $Evidence.metadataHashes[$name]) 'E_THROW_METADATA' 'Original metadata name/hash/load result is absent or not bound to the throwing selection.'}
    Assert-FrameworkPreservation $Case $Evidence
    Assert-FrameworkReady $next $Evidence $Case.knownGood
}
function Test-FrameworkObjectId($Value) { ($Value -is [int] -or $Value -is [long]) -and $Value -ne 0 -and $Value -ge [int]::MinValue -and $Value -le [int]::MaxValue }
function Assert-FrameworkFault($Case,$Evidence,$Definition) {
    Assert-FrameworkScalars $Case
    Require-FrameworkFact ($Case.attempts.Count -in @(1,2) -and $Case.candidate -cne $Case.knownGood -and $Case.fault.kind -ceq $Definition.id -and $Case.fault.candidate -ceq $Case.candidate -and $Case.fault.manifestSha256 -cmatch '^[a-f0-9]{64}$' -and $Case.fault.payloadSha256 -cmatch '^[a-f0-9]{64}$' -and @($Case.fault.inputPaths).Count -gt 0) 'E_FAULT_INPUT' 'Exact fault recipe and input bytes are required.'
    Assert-FrameworkPreservation $Case $Evidence
    $first=$Case.attempts[0];$last=$Case.attempts[-1];$results=@(Get-FrameworkEvents $first 'launch.result')
    Require-FrameworkFact ($results.Count -eq 1) 'E_FAULT_RESULT' 'Actual first-process result is required.'
    $result=$results[0].data
    $failures=if($Definition.kind -ceq 'semantic-reject'){@($result.failures|Where-Object {$_.ReleaseId -ceq $Case.candidate -and $_.Epoch -eq $result.selection.Epoch -and $_.phase -ceq 'ResourceInit' -and $_.Code -cin @('ResourceInitializationFailed','LocalReadyTimeout')})}else{@($result.failures|Where-Object {$_.error -match $Definition.failurePattern -and $_.Code -cin @('PreparationFailed','PreparationCancelled','LaunchPreparationFailed')})}
    Require-FrameworkFact ($failures.Count -gt 0) 'E_FAULT_CAUSE' 'The expected injected failure was not observed.'
    if($Definition.id -cin @('download-cancel','network-interruption')){
        $reason=if($Definition.id -ceq 'download-cancel'){'cancel'}else{'network-interruption'}
        Assert-FrameworkHeldTransfer $Case $Evidence $reason ''
        if($Definition.id -ceq 'download-cancel'){
            $controls=$first.process.acceptance
            Require-FrameworkFact ($controls.runId -ceq $Evidence.runId -and $controls.caseId -ceq $Case.id -and $controls.cancelOnTransfer -is [bool] -and $controls.cancelOnTransfer -eq $true -and @($failures|Where-Object Code -CEQ 'PreparationCancelled').Count -gt 0) 'E_CANCEL_CONTROL' 'Actual native cancellation must bind the explicit CancelOnTransfer input and preparation cancellation result.'
        }
    }
    $candidatePhases=@(Get-FrameworkEvents $first 'release.phase'|Where-Object {$_.data.selection.ReleaseId -ceq $Case.candidate})
    $roots=@($candidatePhases|Where-Object {$_.data.phase -ceq 'ResourceInit'})
    $loads=@($candidatePhases|Where-Object {$_.data.phase -in @('Metadata','Hotfix','LoginReady','Ready')})
    Require-FrameworkFact ($loads.Count -eq 0 -and $result.state.KnownGood -cne $Case.candidate -and $result.state.PreviousGood -cne $Case.candidate) 'E_CANDIDATE_ACTIVATED' 'Failed C loaded metadata/hot code or was promoted.'
    if($Definition.kind -ceq 'semantic-reject'){
        Require-FrameworkFact ($Case.fault.authenticated -eq $true -and $Case.fault.tableSha256 -ceq $Evidence.releases[$Case.candidate].tableSha256 -and $Case.fault.manifestSha256 -ceq $Evidence.releases[$Case.candidate].manifestSha256) 'E_TABLE_AUTHENTICATION' 'Semantically invalid table must have valid authenticated file identity.'
        Require-FrameworkFact ($roots.Count -eq 1 -and $result.resourceAttempts -eq 1 -and $result.metadataCount -eq 0 -and @($result.hotAssemblies).Count -eq 0 -and $result.ready -eq $false -and $result.restartRequired -eq $true) 'E_GF_BOUNDARY' 'GF failure must be one C root attempt, no metadata/hot set/Ready, and require restart.'
        $gf=@(Get-FrameworkEvents $first 'native.error'|Where-Object {$_.data.message -match $Definition.failurePattern -and $_.data.message -notmatch 'TimeoutException|OperationCanceledException|TaskCanceledException|deadline|timed?\s*out|timeout' -and $_.data.selection.ReleaseId -ceq $Case.candidate -and $_.data.selection.ManifestSha256 -ceq $Case.fault.manifestSha256})
        Require-FrameworkFact ($gf.Count -gt 0) 'E_GF_ERROR' 'Actual table-related GF error is required; timeout alone is insufficient.'
        Require-FrameworkFact ($Case.attempts.Count -eq 2 -and $first.processId -ne $last.processId -and $first.attemptId -cne $last.attemptId -and $Case.candidate -cin $result.state.BadReleaseIds -and $result.state.KnownGood -ceq $Case.knownGood) 'E_GF_RECOVERY' 'Quarantine C and recover K in a complete new PID.'
        Require-FrameworkFact (@(Get-FrameworkEvents $first 'release.phase'|Where-Object {$_.data.selection.ReleaseId -ceq $Case.knownGood -and $_.data.phase -ceq 'ResourceInit'}).Count -eq 0) 'E_GF_SAME_PROCESS' 'Same-process C to K resource switching is forbidden.'
    } else {
        Require-FrameworkFact ($roots.Count -eq 0) 'E_CANDIDATE_ROOT' 'Preparation-rejected C must have zero resource-root attempts.'
        if($Case.attempts.Count -eq 2){Require-FrameworkFact ($first.processId -ne $last.processId -and $first.attemptId -cne $last.attemptId) 'E_RECOVERY' 'Recovery must have a new process identity.'}
    }
    Assert-FrameworkReady $last $Evidence $Case.knownGood
}
function Assert-FrameworkPreservation($Case,$Evidence) {
    Assert-FrameworkHashMap $Case.preservation.before $Case.preservation.after 'Known/previous-good payloads'
    $before=@(Get-FrameworkEvents $Case.attempts[0] 'state.before')
    Require-FrameworkFact ($before.Count -eq 1 -and $before[0].data.KnownGood -ceq $Case.knownGood -and -not [string]::IsNullOrWhiteSpace($Case.knownGood)) 'E_PRESERVATION_REFERENCE' 'Original native known-good state must bind this case.'
    $original=$before[0].data
    $firstResult=@(Get-FrameworkEvents $Case.attempts[0] 'launch.result')
    if($firstResult.Count -gt 0){Require-FrameworkFact ($firstResult.Count -eq 1 -and $firstResult[0].data.state.KnownGood -ceq $original.KnownGood -and $firstResult[0].data.state.PreviousGood -ceq $original.PreviousGood) 'E_PRESERVATION_REFERENCE' 'Fault/session execution changed the original K/P references.'}
    foreach($id in @($original.KnownGood,$original.PreviousGood)|Where-Object {$_}){
        $files=$Evidence.releases[$id].files
        Require-FrameworkFact ($files -is [System.Collections.IDictionary] -and $files.Count -gt 0) 'E_PRESERVATION' 'Complete original K/P manifest inventory is required.'
        foreach($name in $files.Keys){Require-FrameworkFact ($Case.preservation.before[($id+'/'+$name)] -ceq $files[$name]) 'E_PRESERVATION' 'Preservation inventory omits or changes original K/P bytes.'}
    }
}
function Assert-FrameworkHeldTransfer($Case,$Evidence,[string]$Reason,[string]$BoundaryUtc) {
    $hold=$Case.transfer.holding;$abort=$Case.transfer.abort;$first=$Case.attempts[0]
    Require-FrameworkFact ($hold -is [System.Collections.IDictionary] -and $abort -is [System.Collections.IDictionary]) 'E_TRANSFER_HOLD' 'Original owned-server holding and terminal records are mandatory; a native update callback alone is insufficient.'
    $candidate=$Evidence.releases[$Case.candidate]
    Require-FrameworkFact ($Case.server.runId -ceq $Evidence.runId -and (Test-FrameworkInteger $Case.server.processId) -and $Case.server.processId -gt 0 -and $Case.server.binarySha256 -cmatch '^[a-f0-9]{64}$' -and $Case.server.endpoint -ceq $Evidence.updateEndpoint) 'E_TRANSFER_SERVER' 'Hold must bind the owned current-run update service and original endpoint.'
    Require-FrameworkFact ($hold.runId -ceq $Evidence.runId -and $hold.caseId -ceq $Case.id -and $hold.transferId -cmatch '^[a-f0-9]{32}$' -and $hold.serverProcessId -eq $Case.server.processId -and $hold.releaseId -ceq $Case.candidate -and $hold.manifestSha256 -ceq $candidate.manifestSha256 -and $hold.sha256 -ceq $candidate.files[$hold.relativePath] -and $hold.expectedBytes -eq $candidate.fileLengths[$hold.relativePath]) 'E_TRANSFER_BINDING' 'Held Content-Length/file bytes must bind the actual authenticated candidate manifest.'
    Require-FrameworkFact ($hold.phase -ceq 'Holding' -and $hold.completed -eq $false -and (Test-FrameworkInteger $hold.bytesSent) -and $hold.bytesSent -gt 0 -and $hold.bytesSent -lt $hold.expectedBytes -and $hold.contentLength -eq $hold.expectedBytes) 'E_TRANSFER_INCOMPLETE' 'The server did not hold a positive incomplete response prefix.'
    $preparation=@(Get-FrameworkEvents $first 'preparation'|Where-Object {$_.data.releaseId -ceq $Case.candidate -and $_.data.manifestSha256 -ceq $hold.manifestSha256})
    $partial=@(Get-FrameworkEvents $first 'download.partial'|Where-Object {$_.data.expectedBytes -eq $hold.expectedBytes -and $_.data.bytes -gt 0 -and $_.data.bytes -le $hold.bytesSent -and $_.data.bytes -lt $_.data.expectedBytes -and ([Uri]$_.data.uri).AbsoluteUri -ceq ([Uri]::new([Uri]$Evidence.updateEndpoint,$hold.relativePath)).AbsoluteUri})
    Require-FrameworkFact ($preparation.Count -gt 0 -and $partial.Count -gt 0 -and $preparation[0].sequence -lt $partial[0].sequence) 'E_TRANSFER_NATIVE' 'Native authenticated preparation and actual positive incomplete transfer must identify the same held file.'
    foreach($field in @('runId','caseId','transferId','serverProcessId','releaseId','manifestSha256','relativePath','sha256','expectedBytes','contentLength','bytesSent')){Require-FrameworkFact ($abort[$field] -ceq $hold[$field]) 'E_TRANSFER_TERMINAL' 'Server terminal record differs from the held original transfer.'}
    Require-FrameworkFact ($abort.phase -ceq 'Abort' -and $abort.reason -ceq $Reason -and $abort.completed -eq $false -and $abort.nativeProcessId -eq $first.processId) 'E_TRANSFER_TERMINAL' 'The server must abort, never complete/resume, for this exact owned native process and purpose.'
    if($BoundaryUtc){Require-FrameworkFact ([DateTimeOffset]::Parse($hold.utc) -le [DateTimeOffset]::Parse($BoundaryUtc) -and [DateTimeOffset]::Parse($abort.utc) -ge [DateTimeOffset]::Parse($BoundaryUtc)) 'E_TRANSFER_AT_BOUNDARY' 'The owned server did not remain held through the actual kill/cancel boundary.'}
}
function Assert-FrameworkKill($Case,$Evidence,$Definition) {
    Assert-FrameworkScalars $Case
    Require-FrameworkFact ($Case.attempts.Count -eq 2) 'E_RECOVERY' 'A killed attempt and a full new recovery process are required.'
    $first=$Case.attempts[0];$next=$Case.attempts[1];$checkpoint=$first.checkpoint;$kill=$Case.kill
    Require-FrameworkFact ($first.processId -ne $next.processId -and $first.attemptId -cne $next.attemptId) 'E_RECOVERY' 'Recovery must use a distinct native PID and attempt.'
    Require-FrameworkFact ($checkpoint.runId -ceq $Evidence.runId -and $checkpoint.caseId -ceq $Case.id -and $checkpoint.attemptId -ceq $first.attemptId -and $checkpoint.processId -eq $first.processId -and $checkpoint.point -ceq $Definition.point -and $checkpoint.purpose -ceq $Definition.statePurpose) 'E_CHECKPOINT' 'Actual checkpoint is absent, stale or at the wrong boundary.'
    $token="$($Evidence.runId):$($Case.id):$($first.attemptId):$($first.processId):$($Definition.point):$($Definition.statePurpose)"
    Require-FrameworkFact ($checkpoint.token -ceq $token -and @(Get-FrameworkEvents $first 'checkpoint.continued').Count -eq 0) 'E_CHECKPOINT' 'Checkpoint token differs or the process resumed before the strong kill.'
    $observations=@(Get-FrameworkEvents $first $Definition.point)
    Require-FrameworkFact ($observations.Count -gt 0 -and ($observations[-1].data|ConvertTo-Json -Depth 30 -Compress) -ceq ($checkpoint.data|ConvertTo-Json -Depth 30 -Compress)) 'E_CHECKPOINT' 'Durable checkpoint differs from its native event.'
    $action=if($Evidence.platform -ceq 'Android'){'am force-stop'}else{'TerminateProcess'}
    Require-FrameworkFact ($kill.processId -eq $first.processId -and $kill.attemptId -ceq $first.attemptId -and $kill.point -ceq $Definition.point -and $kill.action -ceq $action -and $kill.terminal -eq $true) 'E_STRONG_KILL' 'Actual owned OS-process termination is missing.'
    $when=[DateTimeOffset]::Parse($kill.utc)
    Require-FrameworkFact ($when -ge [DateTimeOffset]::Parse($observations[-1].utc) -and $when -le [DateTimeOffset]::Parse($first.process.finishedUtc)) 'E_STRONG_KILL' 'Kill time does not bind to this checkpoint/process.'
    $candidate=$Evidence.releases[$Case.candidate];$boundary=$checkpoint.data
    if($Definition.point -ceq 'rename.before'){Require-FrameworkFact ($boundary.releaseId -ceq $Case.candidate -and $boundary.manifestSha256 -ceq $candidate.manifestSha256) 'E_KILL_SELECTION' 'Rename boundary belongs to another authenticated candidate.'}
    if($Definition.point -in @('state.replace.before','state.replace.after')){Require-FrameworkFact ($boundary.purpose -ceq 'candidate' -and $boundary.releaseId -ceq $Case.candidate -and $boundary.state.Candidate -ceq $Case.candidate -and $boundary.state.Attempt -eq 0 -and $boundary.state.KnownGood -ceq $Case.knownGood) 'E_KILL_SELECTION' 'State replacement did not publish the exact required untried candidate.'}
    if($Definition.point -in @('trial.persisted','hotentry.executing')){
        Require-FrameworkFact ($boundary.selection.ReleaseId -ceq $Case.candidate -and $boundary.selection.ManifestSha256 -ceq $candidate.manifestSha256 -and $boundary.selection.Epoch -gt 0) 'E_KILL_SELECTION' 'Trial/entry boundary belongs to another actual selection.'
        $trials=@(Get-FrameworkEvents $first 'trial.persisted'|Where-Object {$_.data.selection.ReleaseId -ceq $Case.candidate -and $_.data.state.Candidate -ceq $Case.candidate -and $_.data.state.Attempt -eq 1 -and $_.sequence -le $observations[-1].sequence})
        Require-FrameworkFact ($trials.Count -eq 1) 'E_KILL_TRIAL' 'Actual candidate Attempt=1 persistence is required before trial/entry kill.'
    }
    $roots=@(Get-FrameworkEvents $first 'release.phase'|Where-Object {$_.data.phase -ceq 'ResourceInit'})
    $metadata=@(Get-FrameworkEvents $first 'metadata')
    if($Definition.point -ceq 'hotentry.executing'){
        $hot=@(Get-FrameworkEvents $first 'release.phase'|Where-Object {$_.data.phase -ceq 'Hotfix' -and $_.data.selection.ReleaseId -ceq $Case.candidate})
        Require-FrameworkFact ($roots.Count -eq 1 -and $roots[0].data.selection.ReleaseId -ceq $Case.candidate -and $hot.Count -eq 1 -and $hot[0].sequence -lt $observations[-1].sequence -and $metadata.Count -eq $Evidence.metadataNames.Count) 'E_KILL_LOADS' 'Hot-entry kill requires one actual C root/hot load and every original metadata image.'
        foreach($name in $Evidence.metadataNames){Require-FrameworkFact (@($metadata|Where-Object {$_.data.message -ceq "F6_METADATA $($Evidence.playerBuildId) $name OK" -and $_.data.selection.ManifestSha256 -ceq $candidate.manifestSha256 -and $_.sequence -lt $hot[0].sequence}).Count -eq 1) 'E_KILL_LOADS' 'Original metadata did not load under the killed candidate selection.'}
    }else{Require-FrameworkFact ($roots.Count -eq 0 -and $metadata.Count -eq 0) 'E_KILL_EARLY_LOAD' 'This early kill boundary must precede native resource/metadata initialization.'}
    if($Definition.point -ceq 'download.partial'){
        $data=$checkpoint.data
        Require-FrameworkFact ($data.bytes -gt 0 -and $data.expectedBytes -gt $data.bytes -and -not [string]::IsNullOrWhiteSpace($data.path) -and $data.serial -gt 0) 'E_PARTIAL_TRANSFER' 'Require positive actual transferred bytes below authenticated expected length.'
        Assert-FrameworkHeldTransfer $Case $Evidence 'owned-kill' $Case.kill.utc
    }
    Assert-FrameworkPreservation $Case $Evidence
    $launch=@(Get-FrameworkEvents $next 'launch.result')
    Require-FrameworkFact ($launch.Count -eq 1) 'E_RECOVERY' 'One actual recovery result is required.'
    $selected=$launch[0].data.selection.ReleaseId
    $policy=@((Get-FrameworkMatrix).cases|Where-Object {$_.kind -ceq 'strong-kill' -and $_.point -ceq $Definition.point})
    Require-FrameworkFact ($policy.Count -eq 1) 'E_RECOVERY_POLICY' 'Unknown strong-kill recovery boundary.'
    $allowed=@($policy[0].allowedRecovery|ForEach-Object {if($_ -ceq 'KnownGood'){$Case.knownGood}elseif($_ -ceq 'Candidate'){$Case.candidate}else{throw [IO.InvalidDataException]::new('E_RECOVERY_POLICY: Unknown outcome.')}})
    Require-FrameworkFact ($selected -cin $allowed) 'E_RECOVERY_POLICY' 'Recovered selection is not an allowed outcome for this exact kill boundary.'
    Require-FrameworkFact (@($launch[0].data.network|Where-Object {$_.Path -cnotin @('channel.json','channel.sig','release.json','release.sig')}).Count -eq 0) 'E_RECOVERY_REPAIR' 'Recovery must use complete existing payloads, not repair/download candidate payload bytes online.'
    if($selected -ceq $Case.candidate){
        $state=$checkpoint.data.state;$prepared=$Case.preparedCandidate
        Require-FrameworkFact ($Definition.point -ceq 'state.replace.after' -and $state.Candidate -ceq $Case.candidate -and $state.Attempt -eq 0 -and $prepared.releaseId -ceq $Case.candidate -and $prepared.manifestSha256 -ceq $Evidence.releases[$Case.candidate].manifestSha256) 'E_PUBLISHED_CANDIDATE' 'Only a complete candidate whose state was actually published may recover as C.'
        foreach($path in $Evidence.releases[$Case.candidate].files.Keys){Require-FrameworkFact ($prepared.files[$path] -ceq $Evidence.releases[$Case.candidate].files[$path]) 'E_PUBLISHED_CANDIDATE' 'Candidate was not fully captured and verified before the kill.'}
        $prior=@(Get-FrameworkEvents $next 'state.before')
        Require-FrameworkFact ($prior.Count -eq 1 -and $prior[0].data.Candidate -ceq $Case.candidate -and $prior[0].data.Attempt -eq 0 -and $prior[0].data.KnownGood -ceq $Case.knownGood) 'E_PUBLISHED_CANDIDATE' 'New PID did not recover the published untried candidate state.'
    }
    if($policy[0].requireQuarantine){Require-FrameworkFact ($selected -ceq $Case.knownGood -and $Case.candidate -cin $launch[0].data.state.BadReleaseIds -and $null -eq $launch[0].data.state.Candidate) 'E_TRIAL_RECOVERY' 'An unfinished trial must quarantine C and recover K through the existing policy.'}
    Assert-FrameworkReady $next $Evidence $selected
}
function Assert-FrameworkInside($Bounds,$SafeArea,[string]$Name) {
    Require-FrameworkFact ($null -ne $Bounds -and $SafeArea.width -gt 0 -and $SafeArea.height -gt 0 -and $Bounds.right -gt $Bounds.left -and $Bounds.top -gt $Bounds.bottom -and $Bounds.left -ge $SafeArea.x-1 -and $Bounds.right -le $SafeArea.x+$SafeArea.width+1 -and $Bounds.bottom -ge $SafeArea.y-1 -and $Bounds.top -le $SafeArea.y+$SafeArea.height+1) 'E_SAFE_AREA' "$Name lies outside the observed native safe area."
}
function Assert-FrameworkUi($Case,$Definition) {
    Assert-FrameworkScalars $Case
    Require-FrameworkFact ($Case.attempts.Count -eq 1) 'E_UI_ATTEMPT' 'One continuous process must contain this UI action.'
    $operator=$Case.operator
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($operator.name) -and -not [string]::IsNullOrWhiteSpace($operator.environment) -and $operator.action -ceq $Definition.action -and -not [string]::IsNullOrWhiteSpace($operator.utc)) 'E_OPERATOR' 'Manual native action requires operator, UTC, environment and exact action.'
    Require-FrameworkFact (@($operator.screenshots).Count -gt 0) 'E_SCREENSHOT' 'Native screenshots are required.'
    foreach($shot in $operator.screenshots){Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($shot.path) -and $shot.sha256 -cmatch '^[a-f0-9]{64}$') 'E_SCREENSHOT' 'Screenshot path/hash is missing.'}
    $attempt=$Case.attempts[0]
    Require-FrameworkFact ($operator.firstSequence -gt 0 -and $operator.lastSequence -gt $operator.firstSequence -and $operator.lastSequence -le $attempt.events[-1].sequence) 'E_UI_INTERVAL' 'Manual action must bind an exact interval in this native attempt.'
    $samples=@(Get-FrameworkEvents $attempt 'ui.sample'|Where-Object {$_.sequence -ge $operator.firstSequence -and $_.sequence -le $operator.lastSequence})
    $stable=@(Get-FrameworkEvents $attempt 'ui.stable'|Where-Object {$_.sequence -ge $operator.firstSequence -and $_.sequence -le $operator.lastSequence})
    Require-FrameworkFact ($samples.Count -gt 0 -and $stable.Count -gt 0) 'E_UI_STABILITY' 'Actual native sample and stable frames are required.'
    $last=$stable[-1];$snapshot=$last.data.snapshot
    Require-FrameworkFact ($last.data.stableFrames -eq 2) 'E_UI_STABILITY' 'Exactly two stable native frames must be observed.'
    if($Definition.width){Require-FrameworkFact ($snapshot.width -eq $Definition.width -and $snapshot.height -eq $Definition.height) 'E_UI_SIZE' 'Actual client area differs from the required U2 resolution.'}
    $settling=@($samples|Where-Object {$_.data.frame -lt $last.data.frame -and $_.data.snapshot.width -eq $snapshot.width -and $_.data.snapshot.height -eq $snapshot.height})
    Require-FrameworkFact ($settling.Count -gt 0 -and $last.data.frame-$settling[-1].data.frame -eq 2) 'E_UI_STABILITY' 'Stable native geometry is not supported by two frames.'
    $dimensionStart=$settling[0].data.frame
    foreach($sample in $samples){if($sample.data.frame -lt $last.data.frame -and ($sample.data.snapshot.width -ne $snapshot.width -or $sample.data.snapshot.height -ne $snapshot.height)){$dimensionStart=$null};if($null -eq $dimensionStart -and $sample.data.snapshot.width -eq $snapshot.width -and $sample.data.snapshot.height -eq $snapshot.height){$dimensionStart=$sample.data.frame}}
    if($Definition.action -in @('resize','window-drag','rotation','keyboard-rotation','inflight-rotation')){
        Require-FrameworkFact ($null -ne $dimensionStart -and $settling[-1].data.frame-$dimensionStart -le 2) 'E_UI_CONVERGENCE' 'Final geometry did not complete within two stable-size frames; the later stable marker only confirms two unchanged frames.'
    }
    Require-FrameworkFact ($snapshot.layout.Width -eq $snapshot.width -and $snapshot.layout.Height -eq $snapshot.height) 'E_UI_LAYOUT' 'Layout uses stale native dimensions.'
    $mode=if($snapshot.width -lt $snapshot.height){'Portrait'}else{'Landscape'}
    Require-FrameworkFact ($snapshot.layout.mode -ceq $mode) 'E_UI_LAYOUT' 'Actual native layout mode does not follow the current width/height aspect.'
    foreach($scroll in $snapshot.scrolls){Assert-FrameworkInside $scroll.bounds $snapshot.safeArea 'Interactive viewport'}
    $buttons=@($snapshot.buttons|Where-Object active -eq $true)
    Require-FrameworkFact ($buttons.Count -gt 0) 'E_UI_ACTION' 'Actual interactive button geometry is absent.'
    foreach($button in $buttons){Assert-FrameworkInside $button.bounds $snapshot.safeArea $button.name}
    $action=$Definition.action
    if($action -in @('resize','window-drag','rotation','keyboard-rotation','inflight-rotation','keyboard','background','scroll')){
        Require-FrameworkFact (($samples.Count -ge 2) -or ($action -ceq 'resize' -and $samples.Count -ge 1 -and $stable.Count -ge 1)) 'E_UI_CONTINUITY' 'Before and confirmed-after native action observations are required.'
        $first=$samples[0].data.snapshot
        if($action -in @('resize','window-drag','rotation','keyboard-rotation','inflight-rotation','keyboard','background')){Require-FrameworkFact ($first.input.focused -eq $true -and (Test-FrameworkObjectId $first.selected) -and $first.input.length -gt 0) 'E_UI_FOCUS' 'Continuity must start with actual focused nonempty input, not an already-unfocused empty form.'}
        foreach($sample in @($samples)+@($stable)){
            $current=$sample.data.snapshot
            Require-FrameworkFact ($current.view -eq $first.view -and $current.input.id -eq $first.input.id -and $current.input.textSha256 -ceq $first.input.textSha256 -and $current.input.length -eq $first.input.length -and $current.input.caret -eq $first.input.caret -and $current.input.selectionAnchor -eq $first.input.selectionAnchor -and $current.input.selectionFocus -eq $first.input.selectionFocus) 'E_UI_CONTINUITY' 'View, input, text or caret changed during the native action.'
            if($action -ne 'background'){Require-FrameworkFact ($current.selected -eq $first.selected -and $current.input.focused -eq $first.input.focused) 'E_UI_FOCUS' 'Focus changed during the native action.'}
        }
        Require-FrameworkFact ($snapshot.selected -eq $first.selected -and $snapshot.input.focused -eq $first.input.focused) 'E_UI_FOCUS' 'Original focus did not survive or restore after the native action.'
        Require-FrameworkFact (@(Get-FrameworkEvents $attempt 'hot.log'|Where-Object {$_.data.code -ceq 'CLIENT_VIEW'}).Count -eq 1) 'E_PRESENTER' 'Presenter was recreated or not observed.'
    }
    if($action -in @('rotation','keyboard-rotation','inflight-rotation')){
        $directions=@();foreach($sample in $samples){$direction=$sample.data.snapshot.orientation;if(-not $directions.Count -or $directions[-1] -cne $direction){$directions+=$direction}}
        Require-FrameworkFact (($directions -join ',') -ceq 'Portrait,LandscapeLeft,Portrait,LandscapeRight') 'E_ROTATION' 'Actual portrait/left/portrait/right rotation sequence is missing.'
    }
    if($action -in @('keyboard','keyboard-rotation')){Require-FrameworkFact (@($samples|Where-Object {$_.data.snapshot.keyboardVisible -eq $true}).Count -gt 0 -and @($samples|Where-Object {$_.data.snapshot.keyboardVisible -eq $false}).Count -gt 0) 'E_IME' 'Actual IME open/close observations are missing.'}
    if($action -ceq 'background'){
        $pauses=@(Get-FrameworkEvents $attempt 'application.pause')
        Require-FrameworkFact (@($pauses|Where-Object {$_.data.paused -eq $true}).Count -gt 0 -and $pauses[-1].data.paused -eq $false) 'E_BACKGROUND' 'Actual pause and resume are required.'
    }
    if($action -ceq 'window-drag'){Require-FrameworkFact (@($samples|ForEach-Object {"$($_.data.snapshot.width)x$($_.data.snapshot.height)"}|Sort-Object -Unique).Count -ge 3) 'E_DRAG' 'Actual drag must expose intermediate client sizes.'}
    if($action -ceq 'scroll'){
        $before=$samples[0].data.snapshot
        Require-FrameworkFact ($before.scrolls.Count -gt 0 -and $snapshot.scrolls.Count -eq $before.scrolls.Count -and $before.scrolls[0].id -eq $snapshot.scrolls[0].id -and [Math]::Abs($before.scrolls[0].y-$snapshot.scrolls[0].y) -gt 0.01) 'E_SCROLL' 'Actual short-form scrolling did not move the original viewport content.'
        $viewport=$snapshot.scrolls[0].bounds;$rect=@{x=$viewport.left;y=$viewport.bottom;width=($viewport.right-$viewport.left);height=($viewport.top-$viewport.bottom)}
        foreach($button in $buttons){Assert-FrameworkInside $button.bounds $rect 'Scrolled primary action'}
    }
    if($action -ceq 'inflight-rotation'){
        $requests=@(Get-FrameworkEvents $attempt 'session.request');$results=@(Get-FrameworkEvents $attempt 'session.result')
        Require-FrameworkFact ($requests.Count -eq 1 -and $results.Count -eq 1 -and $requests[0].data.request -eq $results[0].data.request -and $requests[0].sequence -lt $samples[0].sequence -and $results[0].sequence -gt $last.sequence) 'E_REQUEST_CONTINUITY' 'Rotation must occur inside one real in-flight request without resubmission.'
    }
}
function Assert-FrameworkAttemptEnvelope($Attempt,$Evidence,[string]$CaseId) {
    Assert-FrameworkScalars $Attempt
    Require-FrameworkFact ($CaseId -cmatch '^[A-Za-z0-9_-]{1,40}$' -and $Attempt.caseId -ceq $CaseId) 'E_CASE_ID' 'Logical case identity differs.'
    Require-FrameworkFact ($Attempt.attemptId -cmatch '^[a-f0-9]{32}$') 'E_ATTEMPT' 'Native-generated attempt identity is missing.'
    Require-FrameworkFact ($Attempt.processId -is [ValueType] -and $Attempt.processId -gt 0 -and $Attempt.processId -eq $Attempt.observedProcessId) 'E_PID' 'Native PID differs from the owned OS process.'
    $owner=$Attempt.process
    Require-FrameworkFact ($owner.processId -eq $Attempt.processId -and $owner.runId -ceq $Evidence.runId -and $owner.caseId -ceq $CaseId -and $owner.playerBuildId -ceq $Evidence.playerBuildId -and $owner.terminal -eq $true) 'E_PROCESS_OWNER' 'Owned process record is missing, stale or nonterminal.'
    $start=[DateTimeOffset]::Parse($owner.startedUtc);$end=[DateTimeOffset]::Parse($owner.finishedUtc)
    Require-FrameworkFact ($end -gt $start) 'E_TIME' 'Process observation interval is invalid.'
    Require-FrameworkFact (@($Attempt.events).Count -gt 1 -and $Attempt.events[0].kind -ceq 'process.start') 'E_EVENTS' 'Native process-start evidence is missing.'
    $sequence=0
    foreach($event in $Attempt.events){
        Require-FrameworkFact ($event.runId -ceq $Evidence.runId -and $event.caseId -ceq $CaseId -and $event.attemptId -ceq $Attempt.attemptId -and $event.processId -eq $Attempt.processId) 'E_EVENT_IDENTITY' 'Stale run, case, attempt or PID in native evidence.'
        Require-FrameworkFact ($event.sequence -eq (++$sequence)) 'E_SEQUENCE' 'Native event sequence is missing, duplicated or reordered.'
        $time=[DateTimeOffset]::Parse($event.utc)
        Require-FrameworkFact ($time -ge $start -and $time -le $end) 'E_TIME' 'Native event lies outside its owned process lifetime.'
    }
}
function Assert-FrameworkAttempt($Attempt,$Evidence,[string]$CaseId) {
    Assert-FrameworkAttemptEnvelope $Attempt $Evidence $CaseId
    $identities=@(Get-FrameworkEvents $Attempt 'native.identity')
    Require-FrameworkFact ($identities.Count -eq 1) 'E_IDENTITY' 'One native identity event is required.'
    $identity=$identities[0].data
    $runtimes=@(Get-FrameworkEvents $Attempt 'native.runtime')
    Require-FrameworkFact ($runtimes.Count -eq 1 -and $runtimes[0].sequence -lt $identities[0].sequence) 'E_RUNTIME' 'Original pre-guard native.runtime facts are required before native.identity.'
    $runtime=$runtimes[0].data
    $expectedRuntime=if($Evidence.platform -ceq 'Android'){'Android'}else{'WindowsPlayer'}
    Require-FrameworkFact ($runtime.platform -ceq $expectedRuntime -and $runtime.unityVersion -ceq $Evidence.identity.UnityVersion -and $runtime.pointerBits -eq 64) 'E_RUNTIME' 'Actual native platform, Unity version or word size differs from the immutable Player.'
    Require-FrameworkFact ($runtime.Contains('applicationId') -and $identity.Contains('applicationId') -and ($null -eq $runtime.applicationId -or $runtime.applicationId -is [string]) -and $identity.applicationId -ceq $runtime.applicationId) 'E_APPLICATION_ID' 'Preserve the actual applicationId value across raw runtime and identity records.'
    Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($Evidence.profilePackageId) -and $runtime.profilePackageId -ceq $Evidence.profilePackageId -and $identity.profilePackageId -ceq $Evidence.profilePackageId) 'E_PROFILE_PACKAGE' 'Baked profilePackageId differs from the original native public profile.'
    if($Evidence.platform -ceq 'Android'){Require-FrameworkFact ($runtime.applicationId -ceq $runtime.profilePackageId) 'E_ANDROID_PACKAGE' 'Android actual OS package ID must equal the baked native profile package.'}
    Require-FrameworkFact ($identity.platform -ceq $runtime.platform -and $identity.pointerBits -eq $runtime.pointerBits) 'E_RUNTIME' 'Native identity contradicts the original runtime facts.'
    foreach($field in @('PlayerBuildId','Platform','Architecture','UnityVersion','ContractVersion','EnvelopeSha256','AotMetadataSetId')) {
        Require-FrameworkFact (-not [string]::IsNullOrWhiteSpace($Evidence.identity[$field]) -and $identity.player[$field] -ceq $Evidence.identity[$field]) 'E_IDENTITY' "Native $field differs from the immutable Player."
    }
    $nativePlatform=if($Evidence.platform -eq 'Android'){'Android'}else{'WindowsPlayer'}
    Require-FrameworkFact ($identity.platform -ceq $nativePlatform -and $identity.pointerBits -eq 64) 'E_NATIVE_PLATFORM' 'Editor or wrong-architecture evidence cannot substitute for the native Player.'
    Require-FrameworkFact ($identity.profileSha256 -cmatch '^[a-f0-9]{64}$') 'E_PROFILE' 'Baked native profile identity is missing.'
    Require-FrameworkFact (@(Get-FrameworkEvents $Attempt 'host.failure').Count -eq 0) 'E_HOST' 'Native acceptance host failed.'
}
function Assert-FrameworkReady($Attempt,$Evidence,[string]$Version) {
    Assert-FrameworkScalars $Attempt
    $templates=@(@('v1','v2')|Where-Object {$Evidence.releases[$Version].dllSha256 -ceq $Evidence.releases[$_].dllSha256 -and $Evidence.releases[$Version].protocolDllSha256 -ceq $Evidence.releases[$_].protocolDllSha256 -and $Evidence.releases[$Version].tableSha256 -ceq $Evidence.releases[$_].tableSha256})
    Require-FrameworkFact ($templates.Count -eq 1) 'E_PAYLOAD_TEMPLATE' 'Ready release must bind one exact original v1/v2 DLL and resource-table template; release ID alone is not a code version.'
    $payloadVersion=$templates[0]
    Assert-FrameworkHashMap $Evidence.releases[$payloadVersion].files $Evidence.releases[$Version].files 'Original complete ready payload template'
    $launches=@(Get-FrameworkEvents $Attempt 'launch.result')
    Require-FrameworkFact ($launches.Count -eq 1) 'E_READY' 'One actual launch result is required.'
    $launch=$launches[0].data;$selection=$launch.selection
    Require-FrameworkFact ($launch.ready -eq $true -and $launch.restartRequired -eq $false -and $launch.state.KnownGood -ceq $Version -and $selection.ReleaseId -ceq $Version) 'E_READY' 'Selected version did not reach durable actual readiness.'
    Require-FrameworkFact ($selection.ManifestSha256 -ceq $Evidence.releases[$Version].manifestSha256 -and $selection.Epoch -gt 0) 'E_SELECTION' 'Selected manifest/epoch differs from the producer.'
    $inventory=$Attempt.slotInventory
    Require-FrameworkFact ($inventory.releaseId -ceq $Version -and $inventory.root -ceq $selection.Root -and $inventory.manifestSha256 -ceq $selection.ManifestSha256 -and $inventory.dllSha256 -ceq $Evidence.releases[$Version].dllSha256 -and $inventory.protocolDllSha256 -ceq $Evidence.releases[$Version].protocolDllSha256 -and $inventory.tableSha256 -ceq $Evidence.releases[$Version].tableSha256) 'E_SLOT' 'Actual selected root/manifest/Game+Protocol DLL/table inventory differs.'
    Require-FrameworkFact ($launch.resourceAttempts -eq 1 -and @($launch.hotAssemblies).Count -eq 2 -and @($launch.hotAssemblies|Sort-Object -Unique).Count -eq 2 -and 'Client.Game' -cin $launch.hotAssemblies -and 'Client.Game.Protocol' -cin $launch.hotAssemblies) 'E_LOAD_COUNT' 'Expected one resource root and one hot assembly set.'
    $phases=@(Get-FrameworkEvents $Attempt 'release.phase')
    foreach($phase in @('ResourceInit','Metadata','Hotfix','LoginReady','Ready')){
        $matches=@($phases|Where-Object {$_.data.phase -ceq $phase})
        Require-FrameworkFact ($matches.Count -eq 1 -and $matches[0].data.selection.ReleaseId -ceq $Version -and $matches[0].data.selection.Root -ceq $selection.Root -and $matches[0].data.selection.ManifestSha256 -ceq $selection.ManifestSha256) 'E_PHASE' "Actual $phase observation is missing, repeated or unbound."
    }
    $metadata=@(Get-FrameworkEvents $Attempt 'metadata')
    Require-FrameworkFact ($metadata.Count -eq $Evidence.metadataNames.Count -and $launch.metadataCount -eq $metadata.Count) 'E_METADATA' 'Every original metadata image must actually load.'
    foreach($name in $Evidence.metadataNames){
        $expected="F6_METADATA $($Evidence.playerBuildId) $name OK"
        Require-FrameworkFact (@($metadata|Where-Object {$_.data.message -ceq $expected -and $_.data.selection.ManifestSha256 -ceq $selection.ManifestSha256}).Count -eq 1 -and $Evidence.metadataHashes[$name] -cmatch '^[a-f0-9]{64}$' -and $inventory.metadata[$name] -ceq $Evidence.metadataHashes[$name]) 'E_METADATA' "Exact original metadata name/hash/result is absent: $name"
    }
    $logs=@(Get-FrameworkEvents $Attempt 'hot.log')
    Require-FrameworkFact (@($logs|Where-Object {$_.data.code -ceq 'CLIENT_CODE_VERSION' -and $_.data.message -ceq $payloadVersion}).Count -eq 1) 'E_BRANCH' 'Actual original payload version branch did not execute.'
    $parser=if($payloadVersion -ceq 'v1'){'BaselineMessage'}else{'AddedMessage'}
    Require-FrameworkFact (@($logs|Where-Object {$_.data.code -ceq 'CLIENT_PARSER_EXECUTED' -and $_.data.message -ceq "$parser list,map,unknown-fields,uint64,hot-generics,empty-registry"}).Count -eq 1) 'E_PARSER' 'Actual required Parser/collections/unknown fields execution is missing.'
    $views=@($logs|Where-Object {$_.data.code -ceq 'CLIENT_VIEW'})
    Require-FrameworkFact ($views.Count -eq 1) 'E_VIEW' 'One actual View/Presenter is required.'
    $view=ConvertFrom-FrameworkJson $views[0].data.message -ObjectOnly
    Require-FrameworkFact ((Test-FrameworkObjectId $view.view) -and (Test-FrameworkObjectId $view.presenter) -and $view.version -ceq ('Framework '+$payloadVersion)) 'E_RESOURCE' 'Actual UI presentation resource version or exact object identity differs from the original ResourcePackageBuilder payload.'
    $entry=@($logs|Where-Object {$_.data.code -ceq 'CLIENT_ENTRY_EXECUTING'})
    Require-FrameworkFact ($entry.Count -eq 1) 'E_ENTRY_ORDER' 'One actual hot-entry execution marker is required.'
    $code=@($logs|Where-Object {$_.data.code -ceq 'CLIENT_CODE_VERSION'})[0]
    $parserEvent=@($logs|Where-Object {$_.data.code -ceq 'CLIENT_PARSER_EXECUTED'})[0]
    $phaseSequence=@{};foreach($phase in $phases){$phaseSequence[$phase.data.phase]=$phase.sequence}
    Require-FrameworkFact ($phaseSequence.ResourceInit -lt $phaseSequence.Metadata -and $phaseSequence.Metadata -lt $metadata[0].sequence -and $metadata[-1].sequence -lt $phaseSequence.Hotfix -and $phaseSequence.Hotfix -lt $entry[0].sequence -and $entry[0].sequence -lt $views[0].sequence -and $views[0].sequence -lt $code.sequence -and $code.sequence -lt $parserEvent.sequence -and $parserEvent.sequence -lt $phaseSequence.LoginReady -and $phaseSequence.LoginReady -lt $phaseSequence.Ready -and $phaseSequence.Ready -lt $launches[0].sequence) 'E_READY_ORDER' 'Metadata, actual hot entry/View/branch/Parser and readiness must follow the real native startup order.'
    $stable=@(Get-FrameworkEvents $Attempt 'ui.stable'|Where-Object {$_.data.stableFrames -eq 2 -and $_.data.snapshot.view -eq $view.view -and (Test-FrameworkObjectId $_.data.snapshot.input.id)})
    Require-FrameworkFact ($stable.Count -gt 0) 'E_UI_READY' 'Actual stable native UI is missing.'
}
function Get-FrameworkAcceptance([System.Collections.IDictionary]$Evidence) {
    $matrix=Get-FrameworkMatrix
    $errors=[Collections.Generic.List[string]]::new()
    # This preparation branch cannot certify native acceptance until collector integration and independent review close.
    if($Evidence.evidenceKind -ceq 'Native'){$errors.Add('E_PREPARATION_GATE: Native acceptance execution/collector qualification remains pending.')}
    if($Evidence.platform -notin $matrix.platforms){$errors.Add('E_PLATFORM: Explicit native platform is required.')}
    if($Evidence.platform -eq 'Android' -and [string]::IsNullOrWhiteSpace($Evidence.deviceId)){$errors.Add('E_DEVICE: Explicit selected Android device is required.')}
    try{Assert-FrameworkContext $Evidence}catch{$errors.Add($_.Exception.Message)}
    foreach($group in @($Evidence.cases|Group-Object id|Where-Object Count -gt 1)){$errors.Add('E_DUPLICATE_CASE: '+$group.Name)}
    foreach($item in $Evidence.cases){if($item.id -cnotin $matrix.cases.id){$errors.Add('E_UNKNOWN_CASE: '+$item.id)}}
    $results=@()
    foreach($definition in $matrix.cases | Where-Object {$Evidence.platform -in $_.platforms}) {
        $item=@{caseId=$definition.id;taskId=$definition.taskId;status='NotRun';errors=@();evidence=@()}
        $matches=@($Evidence.cases|Where-Object {$_.id -ceq $definition.id})
        if($matches.Count -eq 1 -and $matches[0].executed -eq $true){
            $case=$matches[0];$item.status='Fail'
            try{
                Require-FrameworkFact (@($case.attempts).Count -gt 0) 'E_ATTEMPTS' 'Executed case has no actual attempts.'
                foreach($attempt in $case.attempts){Assert-FrameworkAttempt $attempt $Evidence $case.id;$item.evidence+=@($attempt.evidencePaths)}
                switch($definition.kind){
                    'ready' {
                        Require-FrameworkFact ($case.attempts.Count -eq 1) 'E_ATTEMPTS' 'One process is required for each happy-path step.'
                        Assert-FrameworkReady $case.attempts[0] $Evidence $definition.version
                        $downloads=@($case.attempts[0].events|Where-Object {$_.kind -in @('download.start','download.update','download.success','download.failure')})
                        if($definition.offline){Require-FrameworkFact (@($downloads|Where-Object kind -eq 'download.start').Count -gt 0 -and @($downloads|Where-Object {$_.kind -eq 'download.success' -or $_.data.bytes -gt 0}).Count -eq 0) 'E_OFFLINE' 'Actual unavailable-endpoint attempt with no transferred update bytes is required.'}
                        if($definition.online){Require-FrameworkFact (@($downloads|Where-Object {$_.kind -eq 'download.success' -and $_.data.bytes -gt 0}).Count -gt 0) 'E_ONLINE' 'Actual successful online update transfer is required.'}
                        if($definition.cleanInstall){
                            Require-FrameworkFact ($case.install.clean -eq $true -and $case.install.count -eq 1 -and $case.install.playerBuildId -ceq $Evidence.playerBuildId -and @($case.install.evidencePaths).Count -gt 0) 'E_INSTALL' 'Task-owned clean baseline installation evidence is required.'
                            $initial=@(Get-FrameworkEvents $case.attempts[0] 'state.before')
                            Require-FrameworkFact ($initial.Count -eq 1 -and $null -eq $initial[0].data.KnownGood -and $null -eq $initial[0].data.PreviousGood -and $null -eq $initial[0].data.Candidate -and (Test-FrameworkInteger $initial[0].data.Attempt) -and $initial[0].data.Attempt -eq 0 -and $initial[0].data.BadReleaseIds -is [System.Collections.IList] -and $initial[0].data.BadReleaseIds.Count -eq 0) 'E_INSTALL_STATE' 'Actual native state was not empty before clean baseline provisioning.'
                        }
                        if($definition.newProcessAfter){
                            $previous=@($Evidence.cases|Where-Object {$_.id -ceq $definition.newProcessAfter})
                            Require-FrameworkFact ($previous.Count -eq 1 -and $previous[0].attempts.Count -eq 1 -and $previous[0].attempts[0].processId -ne $case.attempts[0].processId -and $previous[0].attempts[0].attemptId -cne $case.attempts[0].attemptId) 'E_RESTART' 'A complete new process after the preceding step is required.'
                        }
                    }
                    {$_ -in @('preparation-reject','semantic-reject')} {Assert-FrameworkFault $case $Evidence $definition}
                    'strong-kill' {Assert-FrameworkKill $case $Evidence $definition}
                    'ui' {Assert-FrameworkReady $case.attempts[0] $Evidence 'v2';Assert-FrameworkUi $case $definition}
                    'session' {
                        Require-FrameworkFact ($case.attempts.Count -eq 1) 'E_SESSION' 'One native process is required.'
                        $attempt=$case.attempts[0];Assert-FrameworkReady $attempt $Evidence 'v2';Assert-FrameworkPreservation $case $Evidence
                        $requests=@(Get-FrameworkEvents $attempt 'session.request');$sessionResults=@(Get-FrameworkEvents $attempt 'session.result')
                        Require-FrameworkFact ($requests.Count -eq 1 -and $sessionResults.Count -eq 1 -and $sessionResults[0].data.request -eq $requests[0].data.request -and $sessionResults[0].data.Succeeded -eq $false -and $sessionResults[0].data.error -ceq $definition.outcome) 'E_SESSION' 'Actual Tok rejection/disconnect must bind to one native request.'
                        $viewLog=@(Get-FrameworkEvents $attempt 'hot.log'|Where-Object {$_.data.code -ceq 'CLIENT_VIEW'})
                        $viewData=ConvertFrom-FrameworkJson $viewLog[0].data.message -ObjectOnly
                        Require-FrameworkFact ($viewData.mode -ceq 'TokDevelopment' -and $case.server.kind -ceq 'TokDemo' -and (Test-FrameworkInteger $case.server.processId) -and $case.server.processId -gt 0 -and $case.server.binarySha256 -cmatch '^[a-f0-9]{64}$') 'E_REAL_TOK' 'Actual Tok development View and owned server binary identity are required.'
                        $states=@(Get-FrameworkEvents $attempt 'session.state'|Where-Object {$_.data.requests -eq 1 -and $_.data.connectionEpoch -gt 0})
                        Require-FrameworkFact (@($states|Where-Object {$_.data.state -ceq 'Authenticating' -and $_.data.requestId -gt 0}).Count -eq 1 -and $states[-1].data.state -ceq 'Disconnected') 'E_SESSION_STATE' 'Real transport authentication and terminal disconnect observations are required.'
                        if($definition.outcome -ceq 'Rejected'){Require-FrameworkFact ($sessionResults[0].data.ServerErrorCode -eq 3) 'E_SERVER_REJECTION' 'Tok rejection must carry the real rejected server code.'}
                        $launch=@(Get-FrameworkEvents $attempt 'launch.result')[0].data
                        Require-FrameworkFact ($launch.state.KnownGood -cnotin $launch.state.BadReleaseIds -and @(Get-FrameworkEvents $attempt 'release.phase'|Where-Object {$_.data.phase -ceq 'RestartRequired'}).Count -eq 0 -and @($case.server.evidencePaths).Count -gt 0) 'E_SESSION_HEALTH' 'Server failure must not classify the locally-ready patch as bad.'
                    }
                    'throw' {Assert-FrameworkThrow $case $Evidence}
                    default {throw [IO.InvalidDataException]::new('E_UNSUPPORTED_CASE: '+$definition.kind)}
                }
                $item.status=if($Evidence.evidenceKind -ceq 'SyntheticUnit'){'UnitPass'}else{'Pass'}
            }catch{$item.errors+= $_.Exception.Message}
        }else{$item.errors+= 'Required native evidence has not been executed.'}
        if($item.status -notin @('Pass','UnitPass')){$errors.Add($(if($item.status -ceq 'NotRun'){'E_NOT_RUN: '}else{'E_CASE_FAILED: '})+$item.caseId)}
        $results+=$item
    }
    $accepted=$errors.Count -eq 0 -and $results.Count -gt 0
    $nativePass=$accepted -and $Evidence.evidenceKind -ceq 'Native'
    $status=if($nativePass){'Pass'}elseif($accepted){'UnitEvidencePass'}else{'Fail'}
    return [ordered]@{schemaVersion=1;task='D2';runId=$Evidence.runId;platform=$Evidence.platform;deviceId=$Evidence.deviceId;status=$status;criteriaAccepted=$accepted;passed=$nativePass;evidenceKind=$Evidence.evidenceKind;errors=@($errors);cases=$results;excluded=$matrix.excluded}
}
