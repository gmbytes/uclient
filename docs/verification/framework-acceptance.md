# Framework acceptance — D2 preparation checkpoint

This checkpoint prepares file-based evaluation and command construction. It is **not D2 completion** and contains no actual Windows/Android acceptance result. `Verify-Framework.ps1` starts no Unity, Player, server or device command. Missing actual evidence remains `NotRun` and makes the aggregate fail.

The source base is `7ba8acd1167d4401c7a0357e47354b7d6fb949f9`. D1 subsequently produced the canonical View GameObject observation correction at `f730e5387aac722c891ab195bdfb2bd0a62709fe`; that correction is not merged into this preparation branch. Final corrected D1 archive approval and explicit controller execution release remain prerequisites.

## Invocation

```powershell
./Tool/Client/Verify-Framework.ps1 -Platform Windows64 -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
./Tool/Client/Verify-Framework.ps1 -Platform Android -DeviceId <selected-LDPlayer-serial> -PlayerArchive <exact-Archive-directory> -BuildResult <exact-build-result.json> -EvidenceFile <current-run-bundle.json>
```

`-OutputDirectory` is optional and must be fresh. The default is a unique directory under `Builds/Framework/<platform>`. The script emits `framework-result.json` and `task-evidence-map.json`; it returns nonzero unless the complete native gate passes. Omitting the exact `BuildResult` or run bundle still creates a failed result with every missing required case explicitly `NotRun`. It never guesses a latest build directory.

`New-FrameworkLaunchCommand` constructs a literal executable/argument array only. It mirrors the bounded `NativeAcceptanceOptions` controls and budgets all actual native evidence suffixes, including the 32-character native attempt identity, against 240 UTF8 bytes. Its `executed` property is always false. It is not an authorization to run those commands.

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

The importer accepts schema 1. Top-level fields include `runId`, `platform`, `deviceId`, `evidenceKind` (`Native` or `SyntheticUnit`), `archiveSha256`, `buildResultSha256`, `nativeBefore`, `nativeAfter`, `releases`, and `cases`. A file reference is `{ "path": "relative/path", "sha256": "64 lowercase hex" }`; it must resolve inside the bundle directory, must not traverse a reparse point, and its current bytes must match. JSON documents reject duplicate/case-aliased keys.

`nativeBefore`/`nativeAfter` reference captured file-to-SHA256 maps matching the original archive. Each `releases.<id>` supplies `manifest`, `dll` and `table` file references. DLL/table hashes must match that manifest. The importer derives preservation requirements from the original manifest file inventory, not a sparse caller-selected subset.

Each case supplies `id`, boolean `executed`, `knownGood`, optional `previousGood`, `candidate`, and `attempts`. Each attempt supplies file references for `events` (the original JSONL), `process` (the owned OS PID, run/case/PlayerBuildId, start/end UTC and terminal state), optional `checkpoint` and `slotInventory`. The importer preserves source paths and event hashes. The native attempt ID comes from the event bytes, not an invented runner ID. Different attempts cannot reuse an event file.

`slotInventory` records the actually selected release/root/manifest/DLL/table and complete original metadata hashes. Additional case documents are file references named `fault`, `preservation`, `operator`, `kill`, `install` and `server` as applicable. Manual action records contain operator, UTC, environment, action, screenshots with hashes, and an exact first/last native event sequence interval. This interval keeps startup observations distinct from the action being checked.

A `Native` bundle also requires an `executionGate` file reference recording controller release, final D1 archive approval, platform, PlayerBuildId, archive hash and producer commit. It is documentary provenance, not a script permission mechanism. Android additionally requires an `environment` document. `SyntheticUnit` can only produce unit-scoped case results; it cannot emit a native acceptance pass.

Preparation-rejected C must have zero C root/metadata/hot-code attempts. K may initialize once in the same process, or after a complete process restart where cancellation terminated launch. Authenticated semantic table failure requires actual GF error evidence bound to C's manifest, one C resource attempt, no metadata/hot assemblies/Ready/promotion, quarantine, preserved K/P files and new-PID K readiness. Timeout alone fails. Strong-kill records bind the actual owned OS action and PID to the native checkpoint; transfer kills require `0 < bytes < expectedBytes`.

## Supplemental tool aggregation

`Test-HotUpdate.ps1`, `Test-UIMatrix.ps1`, and `Test-ProtocolInterop.ps1` accept optional paired `-FrameworkRunId` and `-FrameworkEvidenceDirectory`. Their original assertions and exit conditions remain in place. They write CreateNew `F8`, `U2` or `N3` aggregation receipts binding the original result file hash. These scopes are respectively `LegacyNativeF8`, `EditorGeometry` and `ManagedInterop`, all with `nativeAcceptance: false`. They never fill missing D2 native cases.

## Preparation validation and open work

Run only the pure fixture suites during this phase:

```powershell
pwsh -NoProfile -File Tests/Tools/Test-FrameworkEvidence.ps1
pwsh -NoProfile -File Tests/Tools/Test-FrameworkIO.ps1
pwsh -NoProfile -File Tests/Tools/Test-HotUpdateEvidence.ps1
```

The preparation tests cover strict core identity/hash/branch/Parser/resource/metadata checks, candidate counts, partial-transfer kill/recovery, semantic table failures, selected UI geometry/stability negatives, missing-case aggregation, bounded command construction, raw file hashes, duplicate JSON documents, CreateNew receipts and failed CLI output for missing archives. These are synthetic unit tests only.

Remaining preparation/continuation work is explicit:

1. Integrate the final corrected D1 producer and approved immutable archives through the controller; do not merge another owner's work implicitly.
2. Supply a Delivery-path ThrowOnStart fixture with exact provenance/failure markers. The current `throw-on-start` evaluator intentionally returns `E_PENDING_THROW_FIXTURE`; legacy F8 entry evidence cannot substitute.
3. Complete and independently test the actual collector/orchestrator and full bundle import against final receipt/event shapes, including all native snapshots and preserved prior attempt bytes. This checkpoint constructs commands but does not execute them.
4. Extend fixture coverage to every Android UI action and native session assertion, and harden JSONL parsing/collector schema before releasing actual acceptance. Current importer uses PowerShell JSON parsing for individual JSONL lines; document-level duplicate-key rejection is already present.
5. Run every real native case under an explicit controller release, capture exact OS/device/process and screenshot evidence, then obtain independent D2 review. No current test output satisfies those gates.
