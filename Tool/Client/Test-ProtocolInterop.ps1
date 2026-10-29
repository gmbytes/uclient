[CmdletBinding()]
param([Parameter(Mandatory)][string]$TokRoot,[Parameter(Mandatory)][string]$ProtoRoot,
    [string]$ClientRoot=(Join-Path $PSScriptRoot '../..'),
    [ValidateRange(1,65535)][int]$InternalRpcPort=7001,
    [ValidateSet('Just','Cargo')][string]$Launcher='Just',[string]$FrameworkRunId,[string]$FrameworkEvidenceDirectory)
if([bool]$FrameworkRunId -ne [bool]$FrameworkEvidenceDirectory){throw 'FrameworkRunId and FrameworkEvidenceDirectory must be supplied together.'}
if($FrameworkRunId){. "$PSScriptRoot/Framework-Evidence.ps1";. "$PSScriptRoot/Framework-IO.ps1"}
$ErrorActionPreference='Stop'
$ClientRoot=[IO.Path]::GetFullPath($ClientRoot);$TokRoot=[IO.Path]::GetFullPath($TokRoot);$ProtoRoot=[IO.Path]::GetFullPath($ProtoRoot)
$commTools=[IO.Path]::GetFullPath((Join-Path $ProtoRoot '../tools'))
$runId=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$out=Join-Path $ClientRoot "Builds/ProtocolInterop/$runId";New-Item -ItemType Directory -Force $out | Out-Null
$result=[ordered]@{runId=$runId;status='RUNNING';clientRoot=$ClientRoot;tokRoot=$TokRoot;protoRoot=$ProtoRoot;endpoint='127.0.0.1:7000';steps=@();tools=@{};hashes=@{};server=@{};nativeAndroid='NOT_RUN: requires actual IL2CPP Player; D2 gate';cleanup=@{complete=$false}}
$server=$null;$owned=@{};$oldReal=$env:SGAME_REAL_TOK;$oldProtoc=$env:PROTOC;$oldCargoTarget=$env:CARGO_TARGET_DIR
if($InternalRpcPort -eq 7000){throw 'Internal RPC cannot use client TCP7000'}
function Step([string]$Name,[scriptblock]$Action){
    $log=Join-Path $out "$Name.log";$started=[DateTime]::UtcNow.ToString('o');$global:LASTEXITCODE=0
    try{& $Action *> $log;if($LASTEXITCODE -ne 0){throw "$Name native exit $LASTEXITCODE"};$exitCode=0}
    catch{$exitCode=1;$_ | Out-String | Add-Content -LiteralPath $log;throw}
    finally{$result.steps+=@{name=$Name;startedUtc=$started;exit=$exitCode;log=$log;command=$Action.ToString()}}
}
function Hash([string]$Path){if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Required input missing: $Path"};(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function ObserveOwned {
    $all=@(Get-CimInstance Win32_Process);$changed=$true
    while($changed){$changed=$false;foreach($process in $all){if($owned.ContainsKey([int]$process.ParentProcessId) -and -not $owned.ContainsKey([int]$process.ProcessId)){$owned[[int]$process.ProcessId]=@{command=$process.CommandLine;created=$process.CreationDate.ToUniversalTime().ToString('o')};$changed=$true}}}
}
function OwnedEvidence {
    @($owned.GetEnumerator() | ForEach-Object { @{pid=[int]$_.Key;command=$_.Value.command;createdUtc=$_.Value.created} })
}
try{
    $envelope=Join-Path $ProtoRoot 'tok/client/v1/client.proto';$aot=Join-Path $ClientRoot 'Assets/Scripts/Framework/Protocol/Envelope/Generated/Client.cs'
    $result.hashes.envelope=Hash $envelope;$result.hashes.aotBefore=Hash $aot
    if($result.hashes.envelope -ne '907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583'){throw 'Fixed Frame v3 envelope hash drift'}
    $fixtureManifest=Join-Path $ClientRoot 'Tests/Fixtures/ProtocolInterop/provenance.json'
    $manifest=Get-Content -Raw -LiteralPath $fixtureManifest | ConvertFrom-Json
    foreach($entry in $manifest.files){if((Hash (Join-Path $ClientRoot $entry.path)) -ne $entry.sha256){throw "Fixture artifact drift: $($entry.path)"}}
    foreach($entry in $manifest.sources){if((Hash (Join-Path $ProtoRoot $entry.path)) -ne $entry.sha256){throw "Shared fixture source drift: $($entry.path)"}}
    $result.hashes.fixtureManifest=Hash $fixtureManifest
    $result.hashes.protocolLock=Hash (Join-Path $ProtoRoot 'protocol.lock.json')
    $result.hashes.admission=Hash (Join-Path $ProtoRoot 'client-messages.json')
    $result.sourceFiles=@()
    foreach($root in 'Assets/Scripts/Framework/Protocol','Assets/Scripts/Framework/Network/Core','Tests/Managed','Tests/Fixtures/ProtocolInterop'){
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $ClientRoot $root) -Recurse -File | Where-Object {$_.Extension -in '.cs','.csproj','.props','.json','.pb','.bytes' -and $_.FullName -notmatch '[/\\](obj|bin)[/\\]'}){
            $result.sourceFiles+=@{path=$file.FullName;sha256=(Hash $file.FullName)}
        }
    }
    foreach($relative in 'Assets/Scripts/Framework/Contracts/SessionContracts.cs','Assets/Scripts/Framework/Contracts/ISessionClient.cs','Assets/Scripts/Game/Login/DevelopmentCredentialProvider.cs'){
        $file=Join-Path $ClientRoot $relative
        $result.sourceFiles+=@{path=$file;sha256=(Hash $file)}
    }
    foreach($file in @($PSCommandPath,(Join-Path $commTools 'Test-Managed.ps1'),(Join-Path $commTools 'Test-TokFrames.ps1'),(Join-Path $commTools 'Validate-Boundaries.ps1'),(Join-Path $commTools 'Managed/SGame.Client.Managed.csproj'),(Join-Path $commTools 'TokInterop/build.rs'),(Join-Path $commTools 'TokInterop/src/main.rs'),(Join-Path $commTools 'TokInterop/Cargo.lock'))){
        $result.sourceFiles+=@{path=$file;sha256=(Hash $file)}
    }
    $admission=Get-Content -Raw (Join-Path $ProtoRoot 'client-messages.json') | ConvertFrom-Json
    if(@($admission.request).Count -ne 0 -or @($admission.notify).Count -ne 0){throw 'Formal business admission must remain empty'}
    Step 'protocol-check' {& (Join-Path $PSScriptRoot 'Check-TokProtocol.ps1') -TokRoot $TokRoot -ProtoRoot $ProtoRoot -Check}
    Step 'boundary' {& (Join-Path $commTools 'Validate-Boundaries.ps1') -ClientRoot $ClientRoot -TokRoot $TokRoot -ProtoRoot $ProtoRoot -Target CSharp}
    Push-Location $TokRoot
    try{
        $result.tools.rustc=(& rustc --version | Out-String).Trim();if($LASTEXITCODE -ne 0){throw 'rustc missing'}
        $result.tools.cargo=(& cargo --version | Out-String).Trim();if($LASTEXITCODE -ne 0){throw 'cargo missing'}
        $result.tools.just=(& just --version | Out-String).Trim();if($LASTEXITCODE -ne 0){throw 'just missing'}
        $result.tools.justPath=(Get-Command just -ErrorAction Stop).Source
        $result.hashes.justfile=Hash (Join-Path $TokRoot 'justfile')
        $result.tools.dotnet=(& dotnet --version | Out-String).Trim();if($LASTEXITCODE -ne 0){throw 'dotnet missing'}
        $protoc=Join-Path $ClientRoot 'Builds/tools/protoc/36.2/bin/protoc.exe'
        $result.tools.protocPath=$protoc;$result.tools.protoc=(& $protoc --version | Out-String).Trim();if($LASTEXITCODE -ne 0){throw 'pinned protoc missing'}
        $result.hashes.protoc=Hash $protoc;$result.tools.tokCommit=(& git rev-parse HEAD | Out-String).Trim()
        Step 'rust-csharp-rust' {& (Join-Path $commTools 'Test-TokFrames.ps1') -ClientRoot $ClientRoot -TokRoot $TokRoot -ProtoRoot $ProtoRoot -Target CSharp -OutputRoot $out}
        $config=Join-Path $TokRoot 'examples/demo-game/config/local.toml'
        $result.config=@{original=$config;originalSha256=(Hash $config);internalRpcPort=$InternalRpcPort;diff=@()}
        if($InternalRpcPort -ne 7001){
            $original=[IO.File]::ReadAllText($config);$oldAddress='address = "127.0.0.1:7001"';$newAddress="address = `"127.0.0.1:$InternalRpcPort`""
            if(([regex]::Matches($original,[regex]::Escape($oldAddress))).Count -ne 1){throw 'Default config internal RPC endpoint is ambiguous'}
            $config=Join-Path $out 'tok-local.toml';[IO.File]::WriteAllText($config,$original.Replace($oldAddress,$newAddress))
            $result.config.diff=@("-$oldAddress","+$newAddress")
        }
        $result.config.effective=$config;$result.config.effectiveSha256=Hash $config
        $allListeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue)
        $result.preflightListeners=@($allListeners | Where-Object LocalPort -In 7000,7001,$InternalRpcPort | Select-Object LocalAddress,LocalPort,OwningProcess)
        $listeners=@($allListeners | Where-Object LocalPort -In 7000,$InternalRpcPort)
        if($listeners.Count){throw "Ports 7000/$InternalRpcPort already owned; refusing to replace an unrelated server"}
        $executable=(Get-Command $Launcher.ToLowerInvariant() -ErrorAction Stop).Source
        $launchArguments=if($Launcher -eq 'Cargo'){@('run','-p','demo-game','--locked','--','--config',$config)}else{@('run',$config)}
        $result.server=@{launcher=$Launcher;executable=$executable;arguments=$launchArguments;workingDirectory=$TokRoot;stdout=(Join-Path $out 'tok.stdout.log');stderr=(Join-Path $out 'tok.stderr.log')}
        $env:PROTOC=$protoc
        $env:CARGO_TARGET_DIR=Join-Path $ClientRoot 'Builds/Tok'
        $server=Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory $TokRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'tok.stdout.log') -RedirectStandardError (Join-Path $out 'tok.stderr.log')
        $result.server.rootPid=$server.Id
        $process=Get-CimInstance Win32_Process -Filter "ProcessId=$($server.Id)"
        if(-not $process){throw "Owned $Launcher process exited before process capture; inspect $(Join-Path $out 'tok.stderr.log')"}
        $owned[$server.Id]=@{command=$process.CommandLine;created=$process.CreationDate.ToUniversalTime().ToString('o')}
        $deadline=[DateTime]::UtcNow.AddMinutes(5);$listener=$null;$rpcListener=$null
        do{
            ObserveOwned;$listener=Get-NetTCPConnection -State Listen -LocalPort 7000 -ErrorAction SilentlyContinue | Select-Object -First 1
            $rpcListener=Get-NetTCPConnection -State Listen -LocalPort $InternalRpcPort -ErrorAction SilentlyContinue | Select-Object -First 1
            if($listener -and $rpcListener){break};if($server.HasExited){throw "Owned $Launcher process exited $($server.ExitCode) before server readiness"}
            Start-Sleep -Milliseconds 250
        }while([DateTime]::UtcNow -lt $deadline)
        if(-not $listener -or -not $rpcListener){throw 'Owned Tok demo did not listen on both endpoints within five minutes'}
        if(-not $owned.ContainsKey([int]$listener.OwningProcess) -or -not $owned.ContainsKey([int]$rpcListener.OwningProcess)){throw 'Listener is not a descendant of this run'}
        $result.server.listenerPid=$listener.OwningProcess;$result.server.internalListenerPid=$rpcListener.OwningProcess;$result.server.owned=@(OwnedEvidence)
        $binary=Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        $result.server.binary=$binary.ExecutablePath;$result.server.binarySha256=Hash $binary.ExecutablePath
        $env:SGAME_REAL_TOK='1'
        Step 'real-tok' {& (Join-Path $commTools 'Test-Managed.ps1') -ClientRoot $ClientRoot -TokRoot $TokRoot -ProtoRoot $ProtoRoot -Target CSharp -Filter 'TestCategory=RealTok' -ResultsDirectory (Join-Path $out 'RealTok')}
    }finally{Pop-Location}
    $result.hashes.aotAfter=Hash $aot
    if($result.hashes.aotBefore -ne $result.hashes.aotAfter){throw 'AOT envelope generated source changed'}
    $result.status='PASS'
}catch{$result.status='FAIL';$result.error=$_.Exception.Message;Write-Error $_ -ErrorAction Continue}
finally{
    $env:SGAME_REAL_TOK=$oldReal;$env:PROTOC=$oldProtoc;$env:CARGO_TARGET_DIR=$oldCargoTarget
    if($server){
        ObserveOwned
        foreach($processId in @($owned.Keys | Sort-Object -Descending)){
            $live=Get-CimInstance Win32_Process -Filter "ProcessId=$processId" -ErrorAction SilentlyContinue
            if($live -and $live.CommandLine -eq $owned[$processId].command -and $live.CreationDate.ToUniversalTime().ToString('o') -eq $owned[$processId].created){Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue}
        }
        $result.server.owned=@(OwnedEvidence)
        $remaining=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object {$_.LocalPort -in 7000,$InternalRpcPort -and $owned.ContainsKey([int]$_.OwningProcess)})
        $result.cleanup.complete=($remaining.Count -eq 0)
    }else{$result.cleanup.complete=$true}
    if(-not $result.cleanup.complete){$result.status='FAIL';$result.error='Owned server listener cleanup incomplete'}
    $result.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding utf8NoBOM
    if($FrameworkRunId){Write-FrameworkToolEvidence $FrameworkEvidenceDirectory $FrameworkRunId 'N3' (Join-Path $out 'result.json') 'ManagedInterop'|Out-Null}
    Write-Output (Join-Path $out 'result.json')
}
if($result.status -ne 'PASS'){throw "Protocol interop failed: $($result.error)"}
