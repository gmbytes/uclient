# Protocol interoperability verification

Run from the client root:

```powershell
pwsh -NoProfile -File Tool/Client/Test-ProtocolInterop.ps1 -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto
```

The entry writes `Builds/ProtocolInterop/<runId>/result.json`, checks the fixed Frame v3 envelope/source lock and fixture provenance, runs the explicit CSharp boundary check, then performs Rust emit → C# semantic verification and independent emit → Rust verify/verify-golden. All 12 Frame oneofs and four unsigned64 vectors are covered. Protobuf bytes are compared by parsed fields, including nested/repeated/map/fixed/signed/binary data; uint64 never passes through JSON or floating point. V2's added string field survives V1 parsing/reserialization and is recovered by the V2 static parser.

The script verifies free client TCP7000 and internal gRPC7001 before starting `just run examples/demo-game/config/local.toml` from TokRoot. It records the owned process tree, logs and actual listener owner, tests `DevelopmentCredentialProvider.Create("42")` → `demo:42`, authentication rejection code3, explicit disconnect, and the authenticated idle connection's finite disconnect. It stops only processes whose recorded PID, creation time and command still match. An occupied port, missing pinned tools, absent server, failed tests or incomplete cleanup produces FAIL; cached logs never count as a current pass. Port7001 is never used for a client session.

The standalone commands are:

When the default internal7001 is occupied, the explicit `-InternalRpcPort 17001` option writes a run-local TOML copy whose only change is that internal address. It checks both selected ports and records original/copy hashes and the exact diff. The client remains on TCP7000. If the installed just rejects the upstream justfile, `-Launcher Cargo` explicitly selects the documented recipe-equivalent `cargo run -p demo-game --locked -- --config <effective-config>` from TokRoot; this is recorded as Cargo, never reported as a passing just invocation. Both changes require a visible choice and do not edit Tok source/configuration.

```powershell
pwsh -NoProfile -File ../comm/tools/Test-Managed.ps1 -Target CSharp
pwsh -NoProfile -File ../comm/tools/Validate-Boundaries.ps1 -Target CSharp
pwsh -NoProfile -File ../comm/tools/Test-TokFrames.ps1 -Target CSharp -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto
```

Managed defaults exclude the `RealTok` category. The orchestration explicitly owns the live server and enables/runs that category; directly invoking it without that context fails. Required Protocol and Network/Core roots must exist and contain C# files. The reserved Framework/Core root is included only when present. SessionContracts/ISessionClient are explicit linked files. No Unity/GF source or asmdef is linked into .NET. Boundary checks inspect the actual source roots for Unity/Hotfix reverse dependencies and honor Git's byte-preserved upstream snapshots for repository hygiene.

Editable fixture schemas live only under `comm/proto/tests/fixtures/ProtocolInterop`. The client archive `Tests/Fixtures/ProtocolInterop` contains generated C#, descriptor sets, provenance and vectors for F8/D2. To regenerate intentionally, use pinned `Builds/tools/protoc/36.2/bin/protoc.exe` separately for `Golden/golden.proto`, `V1/compat.proto`, and `V2/compat.proto`, with each shared fixture directory as `--proto_path`, its matching client archive directory as `--csharp_out`, and `--descriptor_set_out=<archive>/schema.pb --include_imports`. Refresh provenance hashes, review the diff, then run the entry. Regeneration and ProtocolGen.Tests are serialized with any N0 full-source snapshot because ProtocolGen fixture tests temporarily create shared `.work` directories.

The formal `comm/proto/client-messages.json` request/notify admissions stay empty. The AOT Envelope source hash must remain unchanged. These tests establish Managed/Rust interoperability and Windows real demo behavior. F8/D2 must separately execute the generated V1/V2 types/static parsers in the same-base-package IL2CPP hot-update Player. A .NET run is not a device pass. Only when an actual native Android Player exists should its run create `adb -s <serial> reverse tcp:7000 tcp:7000`, record that mapping, and remove the mapping it created at the end.
