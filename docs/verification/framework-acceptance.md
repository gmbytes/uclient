# Framework acceptance — current execution status

D2 is incomplete. The current Windows dataset `E:/sgame/Builds/D2/W-20261101T002710348Z` has 15/34 verified cases; the strong-kill stage is continuing. Its one initial installation and original native archive remain unchanged. The Android dataset `E:/sgame/Builds/D2/M-20261031T235052073Z` is historical: 22 cases were collected, then an owned APK experiment interrupted the continuous no-reinstall scope. It is explicitly frozen with `installCount=3` and cannot produce final native acceptance; a fresh complete dataset is required.

The final evidence importer now validates both original D1 raw runs and every native collector source/receipt/event/process binding. The evaluator rereads the exact source bundle and immutable archive before aggregation, so caller approval flags or mutated in-memory cases cannot replace source evidence. Missing cases remain `NotRun`. The complete current tool regression is 19 suites, 350/350, in `Builds/Tests/D2/ToolRegression-20261101T003912187Z`; these are synthetic/tool checks, not 350 native acceptance cases. `Verify-Framework.ps1` remains an evidence-only command and does not launch native processes.

An Android static-manifest hypothesis was tested and rejected as an incomplete explanation: runtime code already enables AutoRotation after the initial portrait frames. The experimental Editor change was reverted. Forced OS rotation could report Landscape while retaining portrait render dimensions; actual gravity commands have not yet produced the required landscape viewport. Neither diagnostic is counted as a passing rotation case. All failed attempts and package replacement receipts remain available.

The material below records earlier preparation checkpoints and is historical; any statement that the importer is unconditionally closed describes that earlier checkpoint.
## Historical D2 preparation checkpoint

This checkpoint prepares file-based evaluation and command construction. It is **not D2 completion** and contains no actual Windows/Android acceptance result. `Verify-Framework.ps1` starts no Unity, Player, server or device command. Missing actual evidence remains `NotRun` and makes the aggregate fail.

The initial source base was `7ba8acd1167d4401c7a0357e47354b7d6fb949f9`. After the first two preparation checkpoints, the controller merged approved producer `28fd6f727b71e8fe23fb5fc4f0abafd151c2ce02` as `7123f0599fe4222593da318e3f8384b5217a99a4`. It includes canonical View GameObject IDs, actual UGUI InputField observations, and platform-specific native identity guards. Third preparation continues from this exact base. Final D1 archive approval and explicit controller execution release remain prerequisites.

The current preparation branch also contains main `911ba5f` through merge `7125ca2`, including the GF callback/destruction fixes. The native qualification scope added in `abe2015` is separate from acceptance: `Invoke-FrameworkNativeQualification` verifies the selected archive/result and every runnable native file, opens effects only during its synchronous body, closes them on success or exception, and rechecks original archive/native bytes before writing `qualification-result.json`. Its result always has `nativeAcceptance=false`; nested scopes and mutated run bindings reject. `Verify-Framework.ps1` and collector acceptance remain closed.

The first actual Android read qualification exposed a Windows capture-handle bug. A real pinned helper regression reproduced it; the output stream now closes before its SHA256 is computed, preserving binary stdout without decoding. The complete tool suite is 321 checks (153 evaluator,15 IO,11 collector,12 ownership,11 transfer,4 throw provenance,34 F8 evaluator,9 qualification,11 Android checkpoint,8 real service/HTTP integration,12 fault-release publishing,14 Android completion/PID,11 execution gate,5 native collection,11 initial-install ownership). This count is tool verification, not executed matrix cases.

Actual Android read qualification is preserved at `Builds/Tests/D2/NativeQualification-Android-20261031T160221099Z`: selected device online, installed APK bytes captured through the receipt-based adapter, SHA256 equal to original Player `31a7aaaf1c0f45a49d82eed7eb0047da`, and original native/archive bytes unchanged. This read-only run did not install, clear data, launch a Player, change a channel or run a required D2 case.

Android start/observe/checkpoint/stop qualification subsequently ran at `Builds/Tests/D2/NativeQualification-Control-20261031T161013210Z`. The prior complete release root was captured first. Actual PID2513/startTicks175335/boot ID and native attempt `dcf84f4bf2f241e89aa77d1b7e29f63f` bound the `hotentry.executing` checkpoint; the adapter reobserved the same process before `am force-stop` and verified termination. Original device bytes, helper receipts, kill receipt and state are retained. A new PID2833 reached actual v2 readiness with48 metadata loads and zero native errors. Its legacy driver's logical label `v1` remains unchanged in raw evidence; selected release was v2.

This control qualification used already-confirmed v2: its durable state had no candidate/attempt and remained byte-identical across the kill. `control-proof.json` explicitly records `candidateRollbackProved=false` and `nativeAcceptance=false`. It qualifies process control, not any of the required candidate strong-kill/recovery cases. Native-client held-transfer, UI/session adapters, Windows control and complete native collector/matrix remain pending.

The release server now receives a file-bound qualification handoff that names the live parent process, original archive/result/runtime package, exact service script and loopback parameters. The child independently validates those inputs, opens its own qualification scope, monitors the original parent and closes the scope when stopping. Standalone launch, wrong scope/endpoint/script hash or parent start time reject before listening. A ready marker is published only after its file handle closes; executable identity is observed after readiness, rather than during the loader race immediately after process creation.

Actual service/held-response qualification is preserved at `Builds/Tests/D2/NativeQualification-Server-20261031T163253609Z`. It served the original signed V2 `Client.Game.dll` (23552 bytes), sent an exact4096-byte prefix, held it, then aborted the connection without sending its remainder. The raw prefix, HTTP headers/outcome, original holding/abort receipts and server exit0 are retained; parent and child both rechecked native/archive bytes unchanged. The requester was explicitly a `QualificationHttpClient`, not an application process, so this still proves no required D2 matrix case.

The earlier `162508895Z` service attempt wrote a transfer abort record but the client timed out because `HttpListenerResponse.Close()` did not terminate the incomplete response. A real HTTP test reproduced that failure, then passed after explicit `Abort()` before publishing the terminal record. Raw failures remain preserved. The seven integration tests also cover startup/stop and five rejected handoffs. Formal native-client transfer progress, UI/session adapters, Windows control and the complete collector/matrix remain pending.

## Fault-release inputs

`Framework-FaultRelease.ps1` provides `New-FrameworkTestRelease` for offline test inputs. It binds the explicit original Player archive/result, verifies both source signatures and channel pin, checks every source payload file plus original metadata/facades, and requires the signer to match the byte-identical archived public profile. It copies only manifest payloads into a fresh nonoverlapping directory; private key bytes/path are not written into its public receipt. The original signed directory is rechecked unchanged. Existing outputs reject.

Supported variants are healthy `None`, bad release signature, bad payload hash, wrong PlayerBuildId, wrong architecture, missing Game DLL, missing changed Bundle, tampered GF table bytes, and a separately signed/hash-valid invalid GF table. Missing Bundle selects a resource whose SHA differs from the explicitly selected known-good manifest, avoiding a false test through native reuse. The semantic-table fixture changes table bytes and updates/signs their manifest identity; its actual GF rejection remains **NotRun** until the native case executes. No second GF parser is introduced.

Actual inputs for both current final Players were generated at `Builds/Tests/D2/ActualFaultInputs-20261031T164954876Z/catalog.json`, with nine unique release IDs per platform. Both missing-Bundle inputs selected `Resources/client/profiles.dat`. The 18 receipts bind source/archive hashes and confirm original source unchanged; they are input provenance, not native acceptance. Healthy input derivation preserves the original resource bytes and displayed template version. ThrowOnStart requires its separately compiled overlay and resource marker rather than relabeling a healthy DLL.

`catalog-verification.json` independently verifies all18 published inputs using the archived public key: each channel signature/pin is valid, only the intended bad-signature release has an invalid release signature, only the four intended missing/tampered payload variants have one inconsistent file, and all original59/48 metadata images remain byte-identical.

Throw overlays were compiled through the canonical `E:/sgame/client` project, explicitly using the D2 worktree's verified overlay directory. The first invocation's missing local overlay directory failed before compilation and restored source; that attempt remains preserved. The corrected run `E:/sgame/client/Builds/Tests/D2/ThrowPatches-20261031T165840961Z` built both patches successfully, rechecked original archives unchanged and restored all source. Windows output is `E:/sgame/Builds/D2Throw/Build_20261031T165859964Z_659ddd1f2aad` (release `throw06fbe36ef29e4466987dcb24019e9720`); Android output is `Build_20261031T170009458Z_fc9a392cbdca` (release `throw0cc2d0e7306546b7bea83ed72cb149f3`). Physical verification confirms each Game DLL differs from healthy V2, each Protocol DLL is exactly the original V2 image, and original59/48 metadata images are unchanged. The builder's strict fixed-reference/AOT compatibility checks passed. Actual native Throw/recovery execution remains NotRun for these candidates.

## Invocation

```powershell
./Tool/Client/Verify-Framework.ps1 -Platform Windows64 -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
./Tool/Client/Verify-Framework.ps1 -Platform Android -DeviceId <selected-LDPlayer-serial> -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
```

`-OutputDirectory` is optional and must be fresh. The default is a unique directory under `Builds/Framework/<platform>`. The script emits `framework-result.json` and `task-evidence-map.json`; it returns nonzero unless the complete native gate passes. Omitting the exact `BuildResult` or run bundle still creates a failed result with every missing required case explicitly `NotRun`. It never guesses a latest build directory.

`New-FrameworkLaunchCommand` constructs a literal executable/argument array only. It mirrors the bounded `NativeAcceptanceOptions` controls and budgets all actual native evidence suffixes, including the 32-character native attempt identity, against 240 UTF8 bytes. Its `executed` property is always false. Android's String extra retains shell quoting around the JSON; actual device validation remains pending. It is not an authorization to run those commands.

The aggregate native-acceptance gate remains closed while the full case bundle and required matrix are incomplete. `Invoke-FrameworkCollection -Execute` now requires an actual NativeAdapter inside the archive-bound NativeExecution scope; a UnitAdapter or unscoped call rejects. Context creation and Collected status grant no acceptance pass.

`native.runtime` is the original pre-guard observation. Its RuntimePlatform, Unity version and pointer width must agree with the immutable Player. `native.identity.applicationId` retains the actual Unity value; `profilePackageId` separately binds the archived native public profile. Windows may report an empty or different actual application ID. Android must report exact package equality. Pointer width proves only word size: the archive reader separately checks original AMD64 PE32+ files and ARM64 APK ELF machine headers. Android Java/per-app process and translation evidence remain a separate gate, never inferred from `pointerBits`.

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

Each release also supplies `protocolDll`. Logical release IDs are not code/resource labels. Readiness binds the exact selected ID, manifest, root and state, then matches an original v1/v2 template using Game DLL, Protocol DLL, native table **and every file in the full inventory**. Unknown or mixed payloads fail. Original code markers are `v1`/`v2`; original resource labels are `Framework v1`/`Framework v2`. A uniquely named complete candidate may therefore execute the original v2 template without being relabeled as release `v2`.

Each case supplies `id`, boolean `executed`, `knownGood`, optional `previousGood`, `candidate`, and `attempts`. Each attempt supplies file references for `events` (the original JSONL), `process` (the owned OS PID, run/case/PlayerBuildId, start/end UTC and terminal state), optional `checkpoint` and `slotInventory`. The importer preserves source paths and event hashes. The native attempt ID comes from the event bytes, not an invented runner ID. Different attempts cannot reuse an event file.

`slotInventory` records the actually selected release/root/manifest/DLL/table and complete original metadata hashes. Additional case documents are file references named `fault`, `preservation`, `operator`, `kill`, `install` and `server` as applicable. Fault, install and server documents retain nonempty `files` arrays of exact raw file references; derived evidence paths are not accepted as a substitute. Manual action records contain operator, UTC, environment, action, screenshots with hashes, and an exact first/last native event sequence interval. This interval keeps startup observations distinct from the action being checked.

A `Native` bundle also requires an `executionGate` file reference recording controller release, final D1 archive approval, platform, PlayerBuildId, archive hash and producer commit. It is documentary provenance, not a script permission mechanism. Android additionally requires an `environment` document. `SyntheticUnit` can only produce unit-scoped case results; it cannot emit a native acceptance pass.

Preparation-rejected C must have zero C root/metadata/hot-code attempts. K may initialize once in the same process, or after a complete process restart where cancellation terminated launch. Authenticated semantic table failure requires actual GF error evidence bound to C's manifest, one C resource attempt, no metadata/hot assemblies/Ready/promotion, quarantine, preserved K/P files and new-PID K readiness. Timeout alone fails. Strong-kill records bind the actual owned OS action and PID to the native checkpoint; transfer kills require `0 < bytes < expectedBytes`.

The original K/P references come from actual `state.before`, not caller-selected labels; both pointers and their complete original payload inventories must survive fault/session execution. Semantic GF rejection may be followed by the existing bounded `LocalReadyTimeout`, but an independent original GF error is still mandatory. A timeout message mentioning “GameFramework” is not malformed-table proof.

## Strong-kill recovery and held transfers

| Actual kill boundary | Allowed recovery | Additional proof |
| --- | --- | --- |
| `download.partial` | K only | Authenticated changed file; native positive partial bytes; owned server still holding an incomplete body through the kill |
| `rename.before` | K only | Exact C release/manifest, before any root/metadata initialization |
| `state.replace.before`, purpose `candidate` | K only | Exact C pending publication, Attempt=0; no C load |
| `state.replace.after`, purpose `candidate` | K or complete C | C requires its full pre-kill payload snapshot and persisted Candidate=C/Attempt=0 in the new PID |
| `trial.persisted` | K only | Actual Attempt=1; C quarantined on recovery |
| `hotentry.executing` | K only | Exact C selection, one C root/hot load, every original metadata image; persisted trial and quarantine |

Recovery may fetch signed channel/manifest envelopes but must not download payload bytes to repair C. This prevents a broad “any ready version” result from hiding crash-state problems. The runner establishes v1 for changed-byte transfer/fault/kill setups and v2 for UI/session setups through the ordinary signed channel and a fresh process. It never rewrites state or clears/reinstalls data between required flow steps. Matching v2 files would be reused from K=v2, so transfer policy construction explicitly rejects that no-download setup.

Each injected C must have a fresh release ID and independently copied payload files; do not reuse the already-installed v1/v2 slot IDs or mutate hard-linked original artifacts. In particular, switching K back to v1 is not enough if the candidate ID still points to an existing complete v2 slot. The original v2 payload template is reused by authenticated hashes, not by reusing its installed slot identity. Signed per-case artifacts are a later controlled input, and no private key is read during preparation.

`Serve-FrameworkReleases.ps1` is a separate D2-only server; the reviewed D1 `Serve-TestReleases.ps1` is unchanged. The new server is blocked by the same preparation gate. Its hold policy binds an authenticated manifest, exact payload path/SHA256 and Content-Length. It sends a positive strict prefix (normally at most 4096 bytes), flushes it, publishes `holding.json`, and sends **no remainder**. Only an exact owned abort control or a bounded timeout ends the response. Timeout never resumes transmission. The opened source bytes are hashed and remain locked during the hold. Only public envelopes and paths listed in the signed inventory are served.

The case bundle's optional `transfer` object contains exact `holding` and `abort` file references. Kill, cancellation and network-interruption cases require the server's unchanged positive byte count, incomplete Content-Length, exact transfer/run/case/server/native PID association and expected abort reason. Kill proof additionally requires the holding interval to include the actual OS termination time. A native download callback from a free-running server is insufficient. Published-C recovery also supplies a `preparedCandidate` snapshot reference.

## Supplemental tool aggregation

`Test-HotUpdate.ps1`, `Test-UIMatrix.ps1`, and `Test-ProtocolInterop.ps1` accept optional paired `-FrameworkRunId` and `-FrameworkEvidenceDirectory`. Their original assertions and exit conditions remain in place. They write CreateNew `F8`, `U2` or `N3` aggregation receipts binding the original result file hash. These scopes are respectively `LegacyNativeF8`, `EditorGeometry` and `ManagedInterop`, all with `nativeAcceptance: false`. They never fill missing D2 native cases.

## Preparation validation and open work

`Framework-Collector.ps1` contains executable local file collection and a code-only orchestration boundary. `Save-FrameworkAttemptEvidence` binds the native attempt directory, exact event identities and owned process record before creating output, then copies original event/checkpoint/completion/screenshot bytes with CreateNew and before/after hashes. `Get-FrameworkSlotSnapshot` checks exact signed inventory paths, lengths and hashes, and captures original manifest/signature bytes; it does not parse GF indexes or replace the native signature verifier. `New-FrameworkCollectionPlan` retains all 34 Windows or 31 Android cases and separate recovery commands. Its unit adapter exercises prepare/start/checkpoint/owned-kill/terminal/capture/recovery ordering using synthetic data. Unit orchestration leaves every native case `NotRun`.

`Bind-FrameworkCollectionPlan` binds the exact producer receipt, complete runnable native inventory and literal launch arguments. `-DiagnosticCapture` can preserve an owned startup guard failure without inventing missing identity fields; it marks the capture ineligible for acceptance. Live first-record reading uses shared file access and never replaces the final durable capture.

`Framework-NativeAdapter.ps1` contains guarded definitions for owned Windows process start/checkpoint/kill/terminal records, Android PID/start-tick/boot-ID/package checks and intent/force-stop operations, exact APK installation/capture, binary command receipts, update-server control, and transfer abort control. Every effect entry point fails before OS observation or execution during preparation. Initial Windows cache cleanup is restricted to the exact observed PlayerBuildId under this user's LocalLow tree, before any required case, after byte-preserving backup; it cannot be used between cases. Android installation refuses an existing package instead of silently overwriting or clearing it. These effect branches have not been live-qualified.

Server input selection first retires an old transfer policy without losing its bytes. A required hold is then installed for the new exact signed input. The client must reach its actual partial-transfer checkpoint before an owned kill/cancel/interrupt. The runner records native termination when applicable, writes the exact server abort control, and retains both original server records before recovery. Clearing a test-server policy is not a native state operation.

UI continuity uses actual nonempty focused input, exact signed object IDs and the same Presenter. Geometry completion is measured at the final changed `ui.sample`, within two frames of final-size metrics; `ui.stable` must then confirm two unchanged frames. Its later confirmation timestamp is not mistaken for the layout-completion timestamp. Native layout mode must follow the actual aspect ratio.

`Tests/Fixtures/Delivery/ThrowOnStart` overrides only `DeliveryVersion.Run` and includes a byte-identical copy of the shared V2 generated `Added.cs`. It runs the unchanged V2 probes, emits `CLIENT_DELIVERY_THROW` with `after login first frame; before ReportReady`, then throws `InvalidOperationException("D2 Delivery ThrowOnStart")`. The existing Entry call site provides that ordering. `throw-provenance.json` binds original and overlay bytes. `Verify-DeliveryThrowFixture.ps1 -OutputDirectory <fresh-path>` checks those bytes and the exact two-line addition; it does not compile or run Unity. The native throw evaluator requires the exact Delivery cause/origin and selected release/epoch/attempt/PID, no Ready/promotion, preserved K/P bytes, and a new-PID K recovery.

Run only the pure fixture suites during this phase:

```powershell
pwsh -NoProfile -File Tests/Tools/Test-FrameworkEvidence.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkIO.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkCollector.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkNativeAdapter.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkTransferPolicy.ps1
pwsh -NoProfile -File Tests/Tools/Test-DeliveryThrowFixture.ps1
pwsh -NoProfile -File Tests/Tools/Test-HotUpdateEvidence.ps1
```

The preparation tests cover strict core identity/hash/branch/Parser/resource/metadata checks, candidate counts, partial-transfer kill/recovery, semantic table failures, all Android UI action shapes, Windows drag, real-Tok-shaped rejection/disconnection, Android ABI/translation/package negatives, missing-case aggregation, bounded command construction, file-bound archive/receipt/JSONL bundle integration, CreateNew collection and failed CLI output for missing archives. Test suites retain machine-readable receipts with explicit synthetic scope. These are unit tests only.

Remaining preparation/continuation work is explicit:

1. Obtain final approved D1 archives and exact build result/receipt paths through the controller. The canonical View source correction is integrated; native artifacts remain a separate gate.
2. Compile/build the Tests-only throw overlay through the authorized later owner and bind the real patch output/provenance. Its Unity compilation and every native throw run are currently `NotRun`.
3. Supply signed case payloads, installation/lease receipts and actual Tok/operator interaction records, then qualify the guarded platform/service adapter definitions against final archives. No controller native/device/server lease has been used during preparation.
4. Resolve and consume the producer's actual Android Java/per-app ABI evidence route. Do not substitute a separate test VM, declare physical ARM compatibility, or infer it from the C# pointer width. Synthetic archive files do not prove native execution, signatures, device behavior or runtime UI convergence.
5. Run every real case under explicit controller release, then obtain independent D2 review. Remove no preparation gate merely because unit tests pass. No current output satisfies native acceptance.

## Preparation checkpoint verified on 2026-10-31

The preserved third preparation change set was resumed at source base `7123f0599fe4222593da318e3f8384b5217a99a4`. All seven tool suites were executed again, with no native effect gate removed:

| Suite | Passing cases |
| --- | ---: |
| Framework evidence evaluator | 153 |
| File/receipt and machine-header IO | 15 |
| Evidence collector and preparation CLI | 11 |
| Native adapter ownership and effect guards | 12 |
| Incomplete-transfer policy | 11 |
| Delivery throw fixture provenance | 4 |
| Existing F8 evidence evaluator | 34 |
| Total | 240 |

All child processes returned exit code 0. PowerShell parsing passed for all 14 changed scripts, and `git diff --check` passed. Raw command logs, machine receipts and source hashes are retained under `Builds/Tests/D2/Resume-20261031T060220056Z-51f32ad7/`. These are synthetic tool checks; the two-platform D2 native matrix remains `NotRun`.

Windows localhost support now retains the native URL in the handoff and binds the service explicitly to127.0.0.1. Its actual service integration test went RED before the mapping, then8/8 GREEN; complete tool regression is280/280. The earlier d967 Windows fault inputs are historical after the new D1 a019 baseline and must be regenerated against that archive before required cases. Android31a7 inputs remain current.

## Android normal completion

`Wait-FrameworkAndroidCompletion` observes the already-owned process and copies original native JSONL only after binding exact run/case/PID/generated attempt and contiguous sequence. It ignores prior processes in the same logical case, rejects mixed/reordered/duplicate terminal records, and distinguishes actual launch.result booleans from host.failure. Observation timeout invents no completion and does not terminate/restart the app.

`Complete-FrameworkAndroidAttempt` rehashes and reparses the capture, reobserves actual PID/kernel start ticks/boot ID and then stops only the owned process. Its receipt has strongKill=false and action `am force-stop after native completion`; it cannot prove a strong-kill boundary. Launch now waits boundedly for PID publication after one am start command. Absence is recorded; no duplicate launch is sent. UTC observation begins at the held launch helper and is labeled `LaunchHelperBeforeNativeStart`; actual kernel start ticks remain the process identity.

Actual qualification NativeQualification-Completion-20261031T174242216Z first queried before PID publication (empty result/exit1). PID3082 was later observed; CompletionResume-20261031T174707662Z resumed that same native run without another launch, captured Ready and verified normal termination. After the fix, NativeQualification-Completion-20261031T174800800Z started PID3287 and passed the complete intent/observation/native-result/normal-stop path. Original native/archive bytes remained unchanged. These are QualificationOnly records, not required matrix cases.
Current Windows fault inputs have now been regenerated against a019 at Builds/Tests/D2/ActualFaultInputs-WindowsLocalhost-20261031T174919174Z/catalog.json. Independent catalog-verification confirms all9 signature/pin/intended-payload-fault behaviors and all59 original metadata images; these replace the earlier d967 Windows inputs. Android31a7 continues to use the Android entries of the earlier catalog. No required native matrix case has yet been executed by the final collector.

## Released execution and native collection

`Read-FrameworkExecutionGate` requires current archive SHA/Player identity/platform, explicit controller release and both original D1 JSONL streams. It rechecks file SHA, generated attempt separation, native runtime/identity, clean Ready, one resource root, exact metadata load names/results, actual version/View markers and the added V2 Parser. An approval boolean alone, stale archive, changed stream or failed D1 version cannot open effects. `Invoke-FrameworkNativeExecution` uses the existing bounded ownership scope, rechecks the gate hash on effects, closes on exception and preserves native/archive hashes. It returns nativeAcceptance=false; that flag is never inferred from context authorization.

Actual current D1 execution files were validated at NativeExecutionRelease-20261031T175916016Z for Windows a019 and Android31a7. They reference the preserved native D1 runs, not reconstructed success booleans. The full verifier still has its independent incomplete-matrix gate.

`Invoke-FrameworkCollection -Execute -NativeAdapter` dispatches preparation, start, checkpoint/UI/session work, owned termination and capture in order. A capture must have its own hashed collector receipt, hashed original JSONL and terminal owned-process record; the event envelope binds run/case/native attempt/PID/time. Adapter/capture failures remain Fail with their error and owned cleanup record. Successful capture is Collected, never Pass. The UnitAdapter preparation path remains SyntheticUnit/NotRun.

Actual NativeCollectorIntegration-20261031T180618310Z ran Android PID3628 through the released scope and public collector entry, captured original Ready events, verified normal termination and imported their file-bound receipt. The case was named collector-smoke/task D2.CollectorIntegration and is not a required matrix case. Its native-collection.json has executed=true/status Collected/nativeAcceptance=false; original app/archive bytes were unchanged. Formal clean-baseline installation, complete candidate fault/recovery/strong-kill, session/UI cases and final evaluation remain pending.
## First formal Android cases

The current matrix dataset is `Builds/Tests/D2/AndroidMatrix-20261031T181741396Z`, runId `d2m-20261031T181741396Z`, Player31a7. Initial preparation verified the currently installed original APK, saved its binary and428 runtime files with inventory, checked no app process was running, rechecked package identity, then removed/reinstalled only that verified task package once. The package was absent immediately before installation. The same original APK hash97121e79... was rechecked after the ready cases. Initial preparation is allowed only in NativeExecution/BeforeBaseline with zero completed required cases and an unconsumed initial boundary; later-phase or foreign-package resets reject.

Four required cases passed their individual evaluator assertions: baseline-offline(PID4124), update-v2(PID4542), restart-v2(PID4911), offline-v2(PID5292). Each has a separate native-generated attempt, original event file, terminal process receipt, actual selected slot inventory and screenshot. The first native state was empty; source-contained V1 reached readiness offline. The same installation then downloaded/executed V2, restarted into V2, and opened V2 with the owned update endpoint unavailable. Installation count remained1 and no reset occurred between cases. Raw per-case partial evaluations and `four-ready-case-proof.json` retain these results.

This is4 of31 Android required cases, not complete native acceptance. The aggregate result intentionally remains Fail because the native final gate/full platform context and the other required cases are incomplete. No scope flag or four-case success converts them to Pass. The owned server80368 stopped with exit0, the owned18765 reverse was removed, and all four app processes were observed terminal. Continue the same dataset and installation for candidate faults/strong kills/session/UI; do not rerun the initial-clean script against this dataset.
## Formal Android preparation-rejection cases

The same dataset/install continued at `stages/preparation-faults-20261031T182916091Z`. Each case first selected the original signed V1 input in a new setup process, reached actual V1 readiness, captured the complete K/P slots, then offered its distinct signed candidate. No reset/reinstall occurred. Seven cases passed original strict `Assert-FrameworkAttempt`/`Assert-FrameworkFault` criteria and were independently replayed from captured event/process/slot/fault/preservation files:

| Case | PID | Candidate |
| --- | --- | --- |
| bad-signature | 5994 | q57deca5953374c5cbe384205862cd3d9 |
| bad-hash | 6667 | q8376d2f8bcb746c0a6f209d47d5a7d2c |
| wrong-player-build | 7336 | q6b2bf17471e8415c8cf1ec3c89bfd6db |
| wrong-architecture | 7984 | q31762fccaa8d4c1aae0f63a0bfa43d23 |
| missing-dll | 8654 | q6b6400be8b244941a66dff26858fb497 |
| missing-bundle | 9397 | q7e634517cd804a03ad4f97bb39cd3939 |
| tampered-version-table | 10131 | q02c3bcc4d1774bae9ba87a56cad1947a |

Each first-process result retained the intended preparation failure, used zero candidate resource roots/metadata/hot code and reached known-good V1 readiness. Complete K/P payload hashes and native references were unchanged. Missing payload requests actually returned404. `independent-criterion-replay.json` binds all seven event hashes and generated attempts. `run-progress.json` now tracks11 completed required cases; original4 ready cases remain in the same run.

All fourteen setup/primary native processes ended. Server10028 stopped with exit0, the owned18765 mapping was removed, and the installed APK remained97121e79... with installation count1. `cleanup-verification.json` and raw process queries retain the terminal state. Signed/hash-valid GF semantic rejection is a separate unexecuted case; so are disk budget, cancellation/interruption, six strong kills, Throw, session/UI and Windows cases. Overall acceptance remains incomplete.
## GF semantic table failure and disk budget

The same run/install continued at `stages/semantic-and-budget-20261031T184510062Z`. The semantic candidate `qabd8008ef26e4696ad32fae5ca751a04` was independently reverified against the archived public key: both signatures and all payload hashes/lengths are valid. Actual primary PID10849/attempt6edd68c6470c4921a82a7679b2a54136 emitted `GameFrameworkException: Header is invalid, need GFP, current D2-` bound to that candidate and manifest28622c41... . It attempted exactly one candidate resource root, loaded zero metadata/hot assemblies, never reached Ready, required restart, quarantined the candidate and retained K=v1/P=v2. The existing startup deadline completed the failed attempt; timeout alone was not used as proof.

Recovery was a complete new PID12265/attempt e3abc44b13744a50977dd7c1c8ea5e3e in the same logical case, using ordinary signed V1 input. It reached actual V1 readiness, preserving complete K/P payloads and references. No same-process root switch occurred. Primary/recovery raw event/process/slot captures and original GF error are retained.

Disk-budget candidate `q124fd15932874b698e5e0ea1451e19ea` ran as PID12946/attempt3050179a5e934504bdb3ca95c7f53b19 with the bounded native lowSpace input; it failed preparation before a candidate root or code load, then reached K=v1 readiness with K/P unchanged. The original physical disk was not filled.

Both cases passed the original strict fault assertions and independent replay from saved files. `independent-criterion-replay.json` also records the separate signature/hash verification. Matrix progress is13/31 Android required cases, installCount1; Windows remains0/34 and overall acceptance remains incomplete. All five setup/primary/recovery processes ended; server58400 exited0, owned reverse removed, original APK rechecked unchanged. Continue the same installation/dataset for cancellation/interruption, strong kills, Throw, session/UI.
## Formal native cancellation and network interruption

The same run/install continued at `stages/held-transfers-20261031T190329695Z` with two fresh healthy V2-derived candidate IDs. Before each case, signed V1 setup reached Ready in a separate process; complete K/P slots were captured before/after. Native and owned-service evidence jointly bound the actual authenticated file, Content-Length and positive incomplete prefix.

Download-cancel candidate `q6e1bbe42bd334931a3b7af6db46dd79c`: primaryPID14539/attempt5d8ab8cc10e24528be2860432106e078 used native cancelOnTransfer=true. The service held the first downloading Protocol DLL at4096/12288 bytes, matching the native download.partial URI/length/count. The native result retained PreparationCancelled; the service then acknowledged abort reason cancel forPID14539 without sending the remainder. A complete new PID14776/attempt5850ce27e2414c64be7df0f170243f45 recovered signed V1 and reached Ready.

Network-interruption candidate `q9834ddaf58d84184a53e79a416e9ac99`: PID15454/attemptd41397a4e1cc406a997d303cc0275bd4 downloaded a real Game DLL prefix4096/23552 bytes. After native positive incomplete progress, the owned service aborted that exact response forPID15454. The actual transport failure rejected preparation before candidate activation and K=v1 reached Ready in that process. Complete K/P payloads and native references remained unchanged in both cases.

Original holding.json/terminal.json, native progress, completion/process/slot captures, fault/server/transfer records and screenshots are retained. Strict original assertions were independently replayed from raw files in `independent-criterion-replay.json`. Progress is15/31 Android cases with installCount1; the full aggregate remains incomplete. Service45948 exited0, the owned reverse was removed, app processes ended and the original APK hash was rechecked unchanged.

Two earlier operator-controller attempts were preserved and not counted: `185709920Z` failed path normalization before candidate launch; `185924063Z` held Game DLL while native cancellation first triggered on the concurrently downloaded Protocol DLL. The URI/length binding rejected that mismatch. Failed source/capture directories were moved only within verified matrix paths and retained. The corrected controller selected the actual first Protocol transfer for cancellation and ignored unrelated native progress by exact URI/length. No runtime assertion was relaxed and no native code was changed.
## Six formal Android strong-kill boundaries

Same run/install continued at `stages/strong-kills-20261031T191346493Z`, with a fresh signed V2-derived candidate for each boundary and ordinary signed V1 setup in a separate process. Each primary blocked at its native-generated durable checkpoint; exact run/case/attempt/PID/token and state-write purpose were validated, kernel start ticks/boot ID were reobserved, and only that process was force-stopped. Original event/checkpoint/process/kill/source bytes remain preserved. Recovery used a complete new PID and no payload downloads.

| Boundary | Primary PID | Recovery PID | Recovered release |
| --- | --- | --- | --- |
| download.partial | 16806 | 17039 | v1 |
| rename.before | 17672 | 17906 | v1 |
| state.replace.before / candidate | 18512 | 18782 | v1 |
| state.replace.after / candidate | 19430 | 19666 | q0817b64aa10d433b95bed752cf97d454 |
| trial.persisted | 20363 | 20740 | v1 |
| hotentry.executing | 21416 | 21807 | v1 |

The download case bound actual Protocol bytes4096/12288 to the owned Holding/Abort service and killed process. The three early preparation/state boundaries loaded zero resource roots/metadata; the entry boundary had exactly one C root/hot set and all48 original metadata images before killing. The last two cases proved durable Attempt=1 before termination and candidate quarantine on the next process.

After candidate state replacement, the complete published candidate was physically captured before recovery. The new process observed Candidate=C/Attempt=0, selected C and reached actual V2-template readiness using only existing bytes (metadata-envelope checks downloaded, no DLL/Bundle payload repair). Other cases recovered K. Complete old K/P payloads were preserved; the preservation snapshot follows the original references dynamically, including the C release that became previous-good after this allowed recovery.

All six original strict kill assertions passed and were independently replayed from saved files in `independent-criterion-replay.json`. Matrix progress is21/31 Android required cases, installation count1; the aggregate remains incomplete. All18 setup/primary/recovery processes ended, server21952 exited0, owned18765 reverse removed and original installed APK97121e79... rechecked unchanged. Throw, session/UI, Windows34 cases and complete bundle/environment/evaluator integration remain pending.