function Assert-HotUpdateEvidence($r) {
    if($r.schemaVersion-ne1 -or $r.task-ne'F8' -or -not$r.runId -or -not$r.playerBuildId){throw 'Invalid F8 identity.'}
    if($r.platform-notin@('Windows64','Android')){throw 'Unsupported required platform.'}
    if($r.platform-eq'Android' -and (-not$r.deviceId -or $r.deviceState-ne'device')){throw 'Android device evidence is missing.'}
    if(($r.nativeBefore|ConvertTo-Json -Compress)-cne($r.nativeAfter|ConvertTo-Json -Compress)){throw 'Native Player changed.'}
    if(-not$r.nativeBefore -or $r.v1DllHash-eq$r.v2DllHash -or $r.v1ResourceHash-eq$r.v2ResourceHash){throw 'Both executable and real native resource must change.'}
    if(@($r.aotNames|Where-Object{$_-like'Client.Game*'}).Count){throw 'Hotfix DLL was compiled into AOT.'}
    if(@($r.metadataNames).Count-eq0){throw 'Original metadata inventory missing.'}
    $pids=@();$by=@{}
    foreach($c in @($r.cases)){
        if($by.ContainsKey($c.caseId) -or $c.runId-cne$r.runId -or $c.playerBuildId-cne$r.playerBuildId -or $c.epoch-le0 -or $c.manifestHash-notmatch'^[a-f0-9]{64}$'){throw 'Stale, duplicate or unbound process evidence.'}
        if($c.processId-le0 -or $pids-contains$c.processId){throw 'A fresh OS process is required.'};$pids+=$c.processId;$by[$c.caseId]=$c
        $expected=@($r.metadataNames|ForEach-Object{"F6_METADATA $($r.playerBuildId) $_ OK"}|Sort-Object)
        if(($expected-join"`n")-cne(@($c.metadata|Sort-Object)-join"`n")){throw 'Metadata load records differ from original archive.'}
        if($c.markers-notcontains'F8_PROBES all'){throw 'Actual hot generic/Parser probes missing.'}
    }
    foreach($id in @('v1','v2','throw','fallback','offline')){if(-not$by.ContainsKey($id)){throw "Missing required case $id"}}
    $v1=$by.v1;$v2=$by.v2;$bad=$by.throw;$fallback=$by.fallback;$offline=$by.offline
    if(-not$v1.ready -or $v1.baselineReads-le0 -or $v1.releaseId-ne'v1' -or $v1.state.KnownGood-ne'v1' -or $v1.newType){throw 'V1 packaged baseline was not installed and executed.'}
    foreach($c in @($v2,$fallback,$offline)){
        if(-not$c.ready -or $c.restartRequired -or $c.releaseId-ne'v2' -or $c.state.KnownGood-ne'v2' -or -not$c.newType -or $c.answer-ne42 -or $c.markers-notcontains'F8_ENTRY_RETURN'){throw 'V2 native execution/readiness evidence incomplete.'}
    }
    if($v1.resourceVersion-eq$v2.resourceVersion -or $v1.manifestHash-eq$v2.manifestHash){throw 'V2 actual resource/release did not change.'}
    if($bad.ready -or -not$bad.restartRequired -or -not$bad.sameProcessReloadRejected -or $bad.releaseId-ne'throw' -or $bad.state.KnownGood-ne'v2' -or $bad.state.BadReleaseIds-notcontains'throw'){throw 'ThrowOnStart did not quarantine without same-process reload.'}
    if($fallback.state.BadReleaseIds-notcontains'throw' -or $fallback.manifestHash-cne$v2.manifestHash -or $offline.manifestHash-cne$v2.manifestHash){throw 'Next-process known-good fallback differs.'}
    if(@($offline.network).Count-eq0 -or @($offline.network|Where-Object{$_.success -or $_.bytes-ne0}).Count){throw 'Endpoint-unavailable case did not make a real failed zero-byte request.'}
}
