# Native hot-update proof (F8)

F8 is in progress. Build-only success and Editor tests do not establish native hot-update acceptance.

Run from the Unity project with PowerShell 7:

```powershell
pwsh -NoProfile -File Tool/Client/Test-HotUpdate.ps1 -Platform Windows64
pwsh -NoProfile -File Tool/Client/Test-HotUpdate.ps1 -Platform Android -DeviceId emulator-5554
```

Each invocation creates an independent `Builds/HotUpdate/<platform>/<runId>` directory. `-BuildOnly` produces a fresh native archive and both compatible patches; `-RunRoot <that run>` subsequently runs those artifacts once. Existing runtime evidence cannot be overwritten. The Android package is `com.sgame.client.hotupdateproof`; the runner refuses to overwrite an already installed scenario package. It installs V1 once and uses only signed release changes thereafter.

For a Windows orchestration-only retry, `-RunRoot <build run> -RetryRuntime` creates a new run, result and installed-store directory while retaining an explicit reference to the same current immutable native build/archive. It preserves the earlier evidence, does not rebuild or modify the Player, and checks its full inventory against the original archive before running. This option is unavailable for Android and never clears or reinstalls its package. Runtime evidence must match the actual owned OS process ID on both platforms.

The proof fixes a public RSA pin and local endpoint into generated native compiler inputs before bounded official HybridCLR generation. Private keys stay in ignored run output. `PublicInputs` and `public-input-hashes.json` retain the generated public profile, source templates and fixed input settings; the profile remains present through native and patch compilation. All seven fixed compiler references must match the original archive byte for byte. Production default trust pins remain empty and fail closed.

Native startup uses `ClientBootstrap.ConfigureLaunchAsync` and `LaunchAsync`, real GF Package initialization, native resource-service catalog loading, the real F6 metadata/assembly loader, integrated login presentation and F7's captured readiness gate. Catalog and login prefab share one `f8/ui` bundle/dependency graph. The source context retains the catalog asset handle until UI disposal. The async composition uses the activation-time local-ready deadline and disposes late returned contexts after cancellation. Existing synchronous ConfigureLaunch remains supported.

The five required independent-process cases are packaged V1 installation, V2 executable type/method returning 42 plus changed GF resource, ThrowOnStart without same-process reloading, next-process V2 fallback, and real update-endpoint-unavailable V2 startup. Endpoint-unavailable means the owned local HTTP server/reverse route is stopped; the application still attempts Check and retains its real failure. It does not mean Wi-Fi or radios were disabled.

`hotupdate-result.json` binds each case to the run, original PlayerBuildId, process, selection epoch and signed manifest hash. It retains actual metadata records, executed markers, selected resource version, durable release state, transfer observations, screenshots/log paths, native before/after inventories and owned cleanup. The evaluator refuses incomplete or stale evidence. `Tests/Tools/Test-HotUpdateEvidence.ps1` exercises its negative cases; these fixtures are evaluator tests only.

Transport `bytes` describes the completed destination file only, and is usable only when `bytesKnown` is true. Filesystem observation errors are recorded independently and never replace transport completion. Endpoint-unavailable evidence additionally requires real GF DownloadStart/Update/Failure observations filtered to the owned transport and correlated by serial/path/run/process: one fresh start at zero, terminal failure, and no positive reported current length. Missing events or unknown file length never count as proof of zero received bytes. Recorder handlers are removed at owned cleanup.

Android evidence must separately identify host/device ABI, APK ARM64 contents, certificate, process architecture and Houdini translation. Physical ARM hardware and iOS remain NotRun. F8 does not complete D2's broad fault/UI/IME/session matrix. Further AOT diagnostics needed by D2 must be finalized before D1's immutable production archive.

All Unity/Player processes are owned and serialized. On this machine Unity uses escalated execution only for the existing local license IPC. No shared licensing process, ADB daemon or emulator-wide restart is part of this proof. Preserve the first failure and use a fresh native archive after any AOT/compiler-input change.

Current implementation evidence and precise results are recorded in the task report under `.superpowers/sdd/2026-10-28-csharp-hotupdate-framework/task-F8-report.md` in the controller's plan workspace.
