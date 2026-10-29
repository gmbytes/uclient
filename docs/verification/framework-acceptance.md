# Framework acceptance — D2 preparation checkpoint

This checkpoint prepares file-based evaluation and command construction. It is **not D2 completion** and contains no actual Windows/Android acceptance result. `Verify-Framework.ps1` starts no Unity, Player, server or device command. Missing actual evidence remains `NotRun` and makes the aggregate fail.

The initial source base was `7ba8acd1167d4401c7a0357e47354b7d6fb949f9`. The controller merged independently approved canonical View GameObject correction `f730e5387aac722c891ab195bdfb2bd0a62709fe` into the first preparation checkpoint as `e917c65419dfca159e7a4f011d5122e75df1c66b`. The second preparation continues from that exact base. Final D1 archive approval and explicit controller execution release remain prerequisites.

## Invocation

```powershell
./Tool/Client/Verify-Framework.ps1 -Platform Windows64 -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
./Tool/Client/Verify-Framework.ps1 -Platform Android -DeviceId <selected-LDPlayer-serial> -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
```

`-OutputDirectory` is optional and must be fresh. The default is a unique directory under `Builds/Framework/<platform>`. The script emits `framework-result.json` and `task-evidence-map.json`; it returns nonzero unless the complete native gate passes. Omitting the exact `BuildResult` or run bundle still creates a failed result with every missing required case explicitly `NotRun`. It never guesses a latest build directory.

`New-FrameworkLaunchCommand` constructs a literal executable/argument array only. It mirrors the bounded `NativeAcceptanceOptions` controls and budgets all actual native evidence suffixes, including the 32-character native attempt identity, against 240 UTF8 bytes. Its `executed` property is always false. Android's String extra retains shell quoting around the JSON; actual device validation remains pending. It is not an authorization to run those commands.

The independent `E_PREPARATION_GATE` blocks native acceptance even when individual fixture assertions pass. `Invoke-FrameworkCollection -Execute` also fails before any native effect. These barriers must remain until actual collector adapters and independent qualification are complete; neither a bundle flag nor completion of the throw overlay can remove them.

## Required matrix

`Tests/Fixtures/Releases/FaultMatrix.json` contains 41 distinct logical definitions: 24 shared cases, ten Windows UI cases, and seven Android UI cases. Thus Windows requires 34 cases and Android requires 31. Supplied `status: Pass` is ignored; executed cases must satisfy actual evidence assertions. Duplicate or unknown case IDs fail.

| Requirement | Required cases/evidence |
| --- | --- |
| D2.Step2 | `baseline-offline`, `update-v2`, `restart-v2`, `offline-v2`; actual selected manifest/root, one resource initialization and hot assembly set, original metadata names/hashes/load results, actual version branch and Baseline/Added Parser marker, resource version and stable UI, distinct restart PID/attempt, unchanged native hashes |
| D2.Step3 | Signature, hash, PlayerBuildId, architecture, missing DLL/Bundle, tampered native table, authenticated GF-semantic table failure, disk budget, cancellation, network interruption |
| D2.Step4 | Six real strong-kill points: `download.partial`, `rename.before`, `state.replace.before`, `state.replace.after`, `trial.persisted`, `hotentry.executing`; exact candidate state purpose where relevant; separate recovery process; ThrowOnStart; actual Tok rejection/disconnect without bad-patch classification |
| D2.Step5 Windows | 720×1280, 1080×1920, 1080×2400, 1536×2048, 1280×720, 1920×1080, 2560×1080, 720×480; real window dragging and short-form scrolling |
| D2.Step5 Android | Portrait→LandscapeLeft→Portrait→LandscapeRight, real IME, IME plus rotation, background/resume, actual safe area, short-form scrolling and a single in-flight request during rotation |
| D2.Step6–7 | Complete two-platform native matrix, immutable final package identity, operator/time/environment/screenshots, then independent review. These are pending. |

Physical ARM hardware and iOS are always separate `NotRun` exclusions for the first-phase scope. Android must identify the selected LDPlayer serial, primary ABI, installed ARM64 ABI, translation environment, certificate, installed APK before/after and native library hashes. No physical ARM claim is made.

## Frozen contracts used

- `Assets/Scripts/Framework/Release/NativeAcceptanceOptions.cs`: safe run/case IDs, supported controls and exact six pause points.
- `Assets/Scripts/Framework/Release/NativeEvidence.cs`: native-generated attempt IDs; CreateNew attempt directories; JSONL run/case/attempt/PID/sequence/UTC; durable checkpoints and positive incomplete transfers.
- `Assets/Scripts/Framework/Bootstrap/NativeClientHost.cs`: `native.identity`, `state.before`, `release.phase`, `metadata`, `hot.log`, `launch.result`, `ui.sample`, `ui.stable`, application lifecycle and session observations.
- `Assets/Scripts/Game/Entry.cs`: `CLIENT_VIEW` contains the View GameObject and Presenter identities. The evaluator compares exact nonzero signed integer identities within one native attempt; it does not normalize negative Unity IDs. It requires the corrected native UI `view` field to identify the same GameObject.
- `Tests/Fixtures/Delivery/V1` and `V2`: actual `CLIENT_CODE_VERSION` and `CLIENT_PARSER_EXECUTED` markers after collection, unknown-field, uint64, hot generic and empty-registry assertions. Generated fixture provenance remains `Tests/Fixtures/Delivery/provenance.json`; editable proto belongs only to `comm/proto`, source commit `93faaa14b6a7a30d8518ed8a10b2411330b1d8b8`.
- `Assets/Scripts/Editor/Build/BuildResult.cs`, `ClientBuild.cs`, and `PlayerBaselineArchive.cs`: explicit successful actual output, Player identity, immutable archive inventories and original metadata.

## Evidence bundle preparation format

The importer accepts schema 1. Top-level fields include `runId`, `platform`, `deviceId`, `evidenceKind` (`Native` or `SyntheticUnit`), `archiveSha256`, `buildResultSha256`, `buildReceipt`, `nativeBefore`, `nativeAfter`, `releases`, and `cases`. `buildReceipt` references the original `ClientBuild` receipt and must bind the exact successful result/output paths. A file reference is `{ "path": "relative/path", "sha256": "64 lowercase hex" }`; it must resolve inside the bundle directory, must not traverse a reparse point, and its current bytes must match. JSON documents and every JSONL record reject duplicate/case-aliased keys, invalid UTF8 and invalid roots. JSONL must retain its final newline and native writer field set. JSON scalar types are preserved, including UTC strings; string booleans/PIDs and floating event sequences are rejected.

`nativeBefore`/`nativeAfter` reference captured file-to-SHA256 maps matching the original archive. Each `releases.<id>` supplies `manifest`, `dll` and `table` file references. DLL/table hashes must match that manifest. The importer derives preservation requirements from the original manifest file inventory, not a sparse caller-selected subset.

Each case supplies `id`, boolean `executed`, `knownGood`, optional `previousGood`, `candidate`, and `attempts`. Each attempt supplies file references for `events` (the original JSONL), `process` (the owned OS PID, run/case/PlayerBuildId, start/end UTC and terminal state), optional `checkpoint` and `slotInventory`. The importer preserves source paths and event hashes. The native attempt ID comes from the event bytes, not an invented runner ID. Different attempts cannot reuse an event file.

`slotInventory` records the actually selected release/root/manifest/DLL/table and complete original metadata hashes. Additional case documents are file references named `fault`, `preservation`, `operator`, `kill`, `install` and `server` as applicable. Fault, install and server documents retain nonempty `files` arrays of exact raw file references; derived evidence paths are not accepted as a substitute. Manual action records contain operator, UTC, environment, action, screenshots with hashes, and an exact first/last native event sequence interval. This interval keeps startup observations distinct from the action being checked.

A `Native` bundle also requires an `executionGate` file reference recording controller release, final D1 archive approval, platform, PlayerBuildId, archive hash and producer commit. It is documentary provenance, not a script permission mechanism. Android additionally requires an `environment` document. `SyntheticUnit` can only produce unit-scoped case results; it cannot emit a native acceptance pass.

Preparation-rejected C must have zero C root/metadata/hot-code attempts. K may initialize once in the same process, or after a complete process restart where cancellation terminated launch. Authenticated semantic table failure requires actual GF error evidence bound to C's manifest, one C resource attempt, no metadata/hot assemblies/Ready/promotion, quarantine, preserved K/P files and new-PID K readiness. Timeout alone fails. Strong-kill records bind the actual owned OS action and PID to the native checkpoint; transfer kills require `0 < bytes < expectedBytes`.

## Supplemental tool aggregation

`Test-HotUpdate.ps1`, `Test-UIMatrix.ps1`, and `Test-ProtocolInterop.ps1` accept optional paired `-FrameworkRunId` and `-FrameworkEvidenceDirectory`. Their original assertions and exit conditions remain in place. They write CreateNew `F8`, `U2` or `N3` aggregation receipts binding the original result file hash. These scopes are respectively `LegacyNativeF8`, `EditorGeometry` and `ManagedInterop`, all with `nativeAcceptance: false`. They never fill missing D2 native cases.

## Preparation validation and open work

`Framework-Collector.ps1` contains executable local file collection and a code-only orchestration boundary. `Save-FrameworkAttemptEvidence` binds the native attempt directory, exact event identities and owned process record before creating output, then copies original event/checkpoint/completion/screenshot bytes with CreateNew and before/after hashes. `Get-FrameworkSlotSnapshot` checks exact signed inventory paths, lengths and hashes, and captures original manifest/signature bytes; it does not parse GF indexes or replace the native signature verifier. `New-FrameworkCollectionPlan` retains all 34 Windows or 31 Android cases and separate recovery commands. Its unit adapter exercises prepare/start/checkpoint/owned-kill/terminal/capture/recovery ordering using synthetic data. Unit orchestration leaves every native case `NotRun`.

`Tests/Fixtures/Delivery/ThrowOnStart` overrides only `DeliveryVersion.Run` and includes a byte-identical copy of the shared V2 generated `Added.cs`. It runs the unchanged V2 probes, emits `CLIENT_DELIVERY_THROW` with `after login first frame; before ReportReady`, then throws `InvalidOperationException("D2 Delivery ThrowOnStart")`. The existing Entry call site provides that ordering. `throw-provenance.json` binds original and overlay bytes. `Verify-DeliveryThrowFixture.ps1 -OutputDirectory <fresh-path>` checks those bytes and the exact two-line addition; it does not compile or run Unity. The native throw evaluator requires the exact Delivery cause/origin and selected release/epoch/attempt/PID, no Ready/promotion, preserved K/P bytes, and a new-PID K recovery.

Run only the pure fixture suites during this phase:

```powershell
pwsh -NoProfile -File Tests/Tools/Test-FrameworkEvidence.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkIO.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkCollector.ps1
pwsh -NoProfile -File Tests/Tools/Test-DeliveryThrowFixture.ps1
pwsh -NoProfile -File Tests/Tools/Test-HotUpdateEvidence.ps1
```

The preparation tests cover strict core identity/hash/branch/Parser/resource/metadata checks, candidate counts, partial-transfer kill/recovery, semantic table failures, all Android UI action shapes, Windows drag, real-Tok-shaped rejection/disconnection, Android ABI/translation/package negatives, missing-case aggregation, bounded command construction, file-bound archive/receipt/JSONL bundle integration, CreateNew collection and failed CLI output for missing archives. Test suites retain machine-readable receipts with explicit synthetic scope. These are unit tests only.

Remaining preparation/continuation work is explicit:

1. Obtain final approved D1 archives and exact build result/receipt paths through the controller. The canonical View source correction is integrated; native artifacts remain a separate gate.
2. Compile/build the Tests-only throw overlay through the authorized later owner and bind the real patch output/provenance. Its Unity compilation and every native throw run are currently `NotRun`.
3. Bind actual platform/device installation, owned-process, update-service/fault-input and Tok/UI adapters to the prepared orchestration boundary. The local collector and synthetic ordering protocol are implemented; live adapters are intentionally disabled/unqualified. No controller lease has been used during preparation.
4. Validate original metadata/archive compatibility and complete native snapshots against the final producer artifacts. Synthetic archive files do not prove IL2CPP, ELF/PE architecture, signatures, device behavior or runtime UI convergence.
5. Run every real case under explicit controller release, then obtain independent D2 review. Remove no preparation gate merely because unit tests pass. No current output satisfies native acceptance.
