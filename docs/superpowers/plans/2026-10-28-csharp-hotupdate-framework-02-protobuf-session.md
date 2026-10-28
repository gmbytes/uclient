# Protobuf 与最小登录会话 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在comm/proto定义并锁定Tok Frame v3兼容源，建立全C#协议生成、有界TCP与登录控制示例，验证C#/Rust互操作，不添加正式游戏业务消息。

**Architecture:** 固定信封和 transport 在 AOT，未来业务类型/Parser注册表在热更程序集；网络宿主接入 GF 生命周期和主线程事件。现有共享工具新增 CSharp 输出分支，不破坏 Lua 消费者；首期会话只执行登录控制，不声称完整恢复已实现。

**Tech Stack:** C#、Google.Protobuf 3.36.2、protoc 36.2、Tok Frame v3、Socket/TcpClient、Unity IL2CPP、NUnit。

**Spec:** [设计基线](../specs/2026-10-28-csharp-hotupdate-framework-design.md)；[总计划](2026-10-28-csharp-hotupdate-framework.md)；[Tok demo接入契约](../../tok-demo-protocol.md)。N0可独立准备，N1及后续依赖F3的稳定契约。

## Global Constraints

- 继承总计划约束；不修改 `E:/sgame/tok` 核心、不更改固定信封。
- 共享源`E:/sgame/comm/proto/tok/client/v1/client.proto`由N0建立，兼容Tok canonical；Frame.version=3，当前SHA `907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583`。客户端生成只读取共享源。
- comm/proto是共享协议唯一构建源；tok固定信封受来源锁控制，不能随意改字段。客户端无可编辑proto副本；正式业务准入表保持空，测试.proto放comm/proto/tests/fixtures并与正式game源隔离，客户端Tests/Fixtures只留生成物/二进制向量。
- 正文最大1,048,576字节，队列每方向64帧/4MiB，完整帧期限15秒，连接与登录各5秒。
- 不发自造心跳、不自动重发未知结果请求、不通过JSON/浮点数保存uint64。
- 项目自有代码全C#；互操作复用已有Rust测试工具，不新增Rust客户端运行代码。
- 简写`Framework/`相对`Assets/Scripts/`；`Tests/EditMode/`和`Tests/PlayMode/`相对`Assets/`；其余相对客户端根，`../comm/`指共享工程。以[工程目录规划](../../project-layout.md)为准。

## Review Focus

1. 信封生成入口绕过锁或新C#目标破坏旧目标：N1同时回归两个输出目标。
2. 分配前未校验长度、首字节一直不到、分片永不完整：N2用假时钟和真实回环验证期限。
3. uint64超过2^53、未知字段、oneof被误解：N1/N4按字段语义验证。
4. 取消/重登的旧响应污染当前会话：N3连接epoch与request_id共同过滤。
5. fixture联通被误称生产认证/恢复支持：N3/N4及U5明确界面和结果范围。

### Task N0：共享Proto定义与Tok兼容锁

**Files:**
- Create: `../comm/proto/tok/client/v1/client.proto`、`../comm/proto/protocol.lock.json`、`../comm/proto/client-messages.json`、`../comm/proto/README.md`、`../comm/proto/game/README.md`、`../comm/proto/LICENSE-Tok.txt`。
- Create: `Tool/Client/Check-TokProtocol.ps1`、`../comm/proto/tests/fixtures/TokContract/Valid`、`../comm/proto/tests/fixtures/TokContract/ChangedFieldNumber`、`../comm/proto/tests/fixtures/TokContract/ChangedOneof`、`../comm/proto/tests/fixtures/TokContract/WrongLock`。
- Modify: `docs/third-party-notices.md`，记录Tok协议来源与实际许可。

**Interfaces:** `Check-TokProtocol.ps1 -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto -Check` 只读校验；成功输出`TASK_PASS N0`和protocol-contract-result.json，源缺失/不兼容/错误锁非零退出。共享来源锁记录canonical相对路径、实际Tok提交、FrameVersion=3、信封原始SHA、网关error.rs原始SHA和采用的错误码。

- [x] **Step 1:** 准备负例fixture：不存在的ProtoRoot、改LoginRequest字段编号、改Frame oneof编号、错来源hash；运行检查入口观察缺失/不兼容时失败，不能零文件通过。
- [x] **Step 2:** 从当前Tok canonical逐字节在comm/proto建立完整client.proto兼容副本，保留所有消息和字段；创建来源锁及request/notify均为空的准入表。game/只写边界说明，不添加业务消息；demo/v1业务协议本Task不纳入运行产物。
- [x] **Step 3:** 实现检查脚本：验证来源锁、完整文件SHA、Frame版本来源以及必要路径，区分工具链锁与共享协议来源锁；Tok源只读，共享信封漂移直接拒绝。记录错误码0/3/7等实际来源，不猜数字。
- [x] **Step 4:** 运行`./Tool/Client/Check-TokProtocol.ps1 -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto -Check`，预期`TASK_PASS N0`；各负例、共享源缺失或空目录必须非零。证据输出到Builds/Tests/N0/<runId>。
- [x] **Step 5:** 分别提交comm协议定义和client检查工具，提交说明`feat(protocol): 定义共享Tok兼容信封与来源锁`。只证明协议源契约；尚未生成C#或联机。


N0验收：client c5f5a79 / comm467e131，24/24用例通过，独立规范与质量审查通过；详见实施进度。

### Task N1：共享生成器 CSharp 目标与 Unity 依赖

**Depends on:** N0、F3。

**Files:**
- Modify: `../comm/tools/ProtocolGen/Program.cs`、`MessageRegistryGenerator.cs`、`../comm/tools/Generate-Protocol.ps1`、`../comm/tools/ProtocolGen/README.md`。
- Create: `../comm/tools/ProtocolGen/CSharpRegistryGenerator.cs`、`../comm/tools/ProtocolGen.Tests/ProtocolGen.Tests.csproj`、`CSharpTargetTests.cs`、`EnvelopeLockTests.cs`。
- Create: `Framework/Protocol/Envelope/Client.Protocol.Envelope.asmdef`、`Framework/Protocol/Envelope/Generated/Client.cs`、`Framework/Protocol/Envelope/source-provenance.json`、`Framework/Protocol/IMessageRegistry.cs`。
- Create: `Assets/Scripts/Game/Protocol/Generated/GameMessageRegistry.cs`、`../comm/proto/tests/fixtures/Protocols/V1`、`../comm/proto/tests/fixtures/Protocols/V2`。
- Reuse/Modify narrowly: `../comm/tools/Dependencies/Restore-Dependencies.ps1`（仅修所需C#依赖落点，保留已有依赖边界）。

**Interfaces:**
- `Generate-Protocol.ps1 -Target CSharp|Lua -ProtoRoot E:/sgame/comm/proto -ProtocolLock E:/sgame/comm/proto/protocol.lock.json -ClientMessages <json> -Output <directory> [-Baseline <directory>] [-Check]`；新客户端显式选CSharp，其他目标用隔离fixture回归。
- CSharp目标输出生成的`*.cs`、`GameMessageRegistry.cs`、`descriptor.bytes`、`protocol-manifest.json`；来源来自共享协议锁，protoc/runtime版本来自工具链锁。固定信封`--envelope-output`也校验同一来源锁，不把旧工具锁内的Tok提交号写成新来源。
- `IMessageRegistry.TryGetParser(uint messageId,out Google.Protobuf.MessageParser parser):bool`；`TryGetResponseId(uint requestId,out uint responseId):bool`。不使用运行时扫描程序集、字符串反射找Parser。

- [x] **Step 1:** 写生成器测试：空业务准入→合法空registry；fixture request/response/notify ID正确；重复ID/类型/方向冲突拒绝；修改信封任一字节两个生成入口都拒绝；共享必需源缺失/为空失败且保持旧输出完整。
- [x] **Step 2:** `dotnet test ../comm/tools/ProtocolGen.Tests/ProtocolGen.Tests.csproj --filter "CSharpTargetTests|EnvelopeLockTests"`，确认新目标/锁校验缺失时失败；测试项目沿用ProtocolGen目标框架并锁定测试依赖。
- [x] **Step 3:** 实现`--target csharp`和显式共享协议锁参数，复用DescriptorValidator、SchemaCompatibility、准入和hash门禁；所有入口统一comm/proto，不从客户端或缓存隐式回退读取协议。protoc36.2生成C#，parser/ID映射由同一准入表生成；固定信封和业务输出分开，禁止业务声明tok.client.v1包。
- [x] **Step 4:** 用共享restore安装Google.Protobuf3.36.2及必要Unsafe，避免覆盖Unity已有Memory/Buffers/Numerics；记录依赖hash。生成AOT固定信封和热更空registry，Unity编译通过；fixture V1→V2只允许兼容字段增量，非法变更拒绝。
- [x] **Step 5:** 重跑测试，并用显式fixture源对其他工具目标做隔离回归；确认客户端只保留生成C#、协议源统一comm/proto后提交`feat(protocol): 新增CSharp协议与静态注册表生成`。不增加正式game/业务准入。

### Task N2：有界 TCP 分帧与 GF 主线程宿主

**Files:**
- Create: `Framework/Network/Core/FrameCodec.cs`、`IFrameTransport.cs`、`TcpFrameTransport.cs`、`TransportOptions.cs`、`TransportClock.cs`。
- Create: `Framework/Network/Unity/TokNetworkComponent.cs`、`MainThreadFrameQueue.cs`。
- Create: `Tests/EditMode/FrameCodecTests.cs`、`TransportDeadlineTests.cs`、`Tests/PlayMode/TcpLoopbackTests.cs`。

**Interfaces:**
- `FrameCodec.Encode(Tok.Client.V1.Frame frame):byte[]`；`FrameCodec.Decode(ReadOnlySpan<byte> framed):Tok.Client.V1.Frame`；`FrameCodec.ReadLength(ReadOnlySpan<byte> prefix):int`，返回前校验0<length<=1,048,576。Decode要求字节恰好包含一个前缀和声明的正文，不接受多帧/尾部垃圾。
- `IFrameTransport.ConnectAsync(string host,int port,CancellationToken token):Task`；`SendAsync(ReadOnlyMemory<byte> frame,CancellationToken token):Task`；`ReadAsync(CancellationToken token):Task<byte[]>`；`Close():void`。返回/发送字节含固定大端前缀，只有FrameCodec处理protobuf。
- `TransportOptions` 包含5秒ConnectTimeout、15秒ReadFrameTimeout/WriteFrameTimeout、64队列项和4MiB队列字节限额；使用单调时钟。`TokNetworkComponent` 负责主线程消费和退出取消，不把Unity API带入Core。

- [x] **Step 1:** 写 `RejectsLengthBeforeAllocating`（0、1MiB+1、uint.MaxValue）、`AcceptsExactOneMiB`、`ReadsSplitAndCoalescedFrames`、`EofInFrameIsError`；断言分配计数在非法长度时为0。
- [x] **Step 2:** 写 `DeadlineIncludesWaitingForFirstByte`：假时钟15秒无首字节超时；`DripBytesCannotExtendFrameDeadline`；`QueueNeverExceedsBothLimits`：超限返回Backpressure并关闭/拒绝当前连接，不无限等待。运行N2 EditMode，确认红灯。
- [x] **Step 3:** 实现一个读取循环、一个串行写循环和有界队列；每帧固定总期限，取消通过关闭socket终止阻塞I/O。使用SemaphoreSlim/受锁Queue等Unity支持的BCL，不隐式依赖System.Threading.Channels新包。禁止在收包线程调用GF事件或UI。
- [x] **Step 4:** 实现组件宿主和主线程有界队列；应用退出/切场景只清理自身连接，异步读取带epoch。运行 `./Tool/Client/Test-Client.ps1 -Task N2 -Mode EditMode` 与PlayMode，预期 `TASK_PASS N2`；真实回环测试包含IPv4、IPv6可用时覆盖，不可用明确跳过原因。
- [x] **Step 5:** 提交 `feat(network): 实现有界Protobuf帧传输`。本Task只证明transport，不证明认证。

### Task N3：最小登录会话状态机

**Files:**
- Create: `Framework/Network/Core/SessionClient.cs`、`RequestIdSequence.cs`、`SessionOptions.cs`。
- Create: `Assets/Scripts/Game/Login/DevelopmentCredentialProvider.cs`。
- Create: `Tests/EditMode/SessionClientTests.cs`、`RequestIdTests.cs`、`Tests/Fixtures/LoginServer/Program.cs`、`LoginServer.csproj`。
- Modify: F3 `Framework/Contracts/ISessionClient.cs` 仅补注释，不改变已定义签名/DTO。

**Interfaces:** `SessionClient : ISessionClient` 使用F3的LoginInput/LoginResult/SessionIdentity/SessionState；`RequestIdSequence.Next():ulong` 溢出拒绝且不回零。`DevelopmentCredentialProvider.Create(string account):LoginInput` 只接受正整数，构造 `credential=demo:<id>`、空resumeToken。测试服务 `dotnet run --project Tests/Fixtures/LoginServer -- --port <port> --case success|reject|timeout|half-frame`。

- [x] **Step 1:** 写 `LoginCredentialAndTokenAreExclusive`、`ConcurrentLoginReturnsBusy`、`LoginFailureNeverBecomesAuthenticated`；成功需匹配request_id、version3、error_code0及有效Identity，控制错误映射有限LoginError。
- [x] **Step 2:** 写 `CancelledOldResponseCannotChangeNewSession`、`DisconnectRestoresUIState`、`RequestIdsAbove2Pow53RemainExact`（输入9,007,199,254,740,993，往返不变）、`OverflowRequiresNewSession`；运行N3 EditMode确认红灯。
- [x] **Step 3:** 实现Idle→Connecting→Authenticating→Authenticated/Disconnected；Connect和Login各5秒，使用同一连接epoch过滤。按N0锁定错误码映射0成功/3认证拒绝/7超时，未知非零保留ServerErrorCode且不得判成功。未匹配消息为ProtocolError；首期未支持的业务/恢复下行明确断开，不虚假ACK、不静默丢失。
- [x] **Step 4:** 创建C#回环登录fixture并覆盖成功、拒绝、超时、取消、错误版本、半帧、服务器关闭、登录后空闲断开；token仅保内存，日志不含credential/token。登录后停止在演示状态，不实现业务初始化和自动Resume。
- [x] **Step 5:** 运行 `./Tool/Client/Test-Client.ps1 -Task N3 -Mode EditMode` 与PlayMode，检查清理后无残留socket/重复事件。提交 `feat(session): 接通最小Protobuf登录控制`。

### Task N4：C#/Tok 双向互操作与热更协议验证

**Files:**
- Modify: `../comm/tools/Managed/SGame.Client.Managed.csproj`、`../comm/tools/Validate-Boundaries.ps1`、`../comm/tools/Test-Managed.ps1`、`../comm/tools/Test-TokFrames.ps1`，新增显式CSharp测试目标，不复活Lua客户端依赖。
- Create: `Tests/Managed/Client.CSharp.Tests.csproj`、`TokFixtureTests.cs`、`ProtocolCompatibilityTests.cs`、`Tool/Client/Test-ProtocolInterop.ps1`。
- Create: `docs/verification/protocol.md`；Reuse: 已有 `../comm/tools/TokInterop` 的Rust emit/verify命令，不改Tok核心。

**Interfaces:** `Test-ProtocolInterop.ps1 -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto` 输出 `Builds/ProtocolInterop/<runId>/result.json`。Managed项目显式Include实际纯C#根`Assets/Scripts/Framework/Protocol`、`Network/Core`及纯SessionContracts/ISessionClient等必要文件；`Framework/Core`是预留目录，有实际代码时才纳入。不能宽泛Include带Unity/GF的文件，不把asmdef或Unity组件链接到.NET测试。边界检查必须使用新根，必需根缺失/扫描零文件直接失败；不能为满足扫描建立占位代码。

- [ ] **Step 1:** 写Golden fixture字段断言，覆盖Frame所有oneof、控制请求/回应、bytes正文、uint64边界；新增字段V2经V1解析后保留未知字段，再经V2读取仍在。比较语义字段，不依赖任意Protobuf序列化输出的规范顺序。
- [ ] **Step 2:** 运行新Managed测试，确认缺生成物/错误Frame实现时失败；验证新根缺失或被注入UnityEditor/Hotfix反向引用时边界检查失败，防止仍扫旧路径而假通过。更新所有测试.proto读取者到comm/proto/tests/fixtures，客户端Tests/Fixtures与Tests/Managed只保留生成物、向量和C#测试。
- [ ] **Step 3:** 执行现有Rust emit→新C#逐字段读取→C#emit→现有Rust verify/verify-golden；所有路径和工具版本进入result.json，Rust工具链或protoc缺失报阻塞，不把缓存日志记通过。
- [ ] **Step 4:** 在Tok根执行`just run examples/demo-game/config/local.toml`持续运行demo，Windows连接127.0.0.1:7000、账号42→demo:42，验证成功/拒绝/断线；拒绝误用7001内部gRPC。AndroidUSB联调先`adb -s <serial> reverse tcp:7000 tcp:7000`并记录，结束移除映射。服务未运行/依赖未满足时真实联调项保持未完成，不修改Tok核心或虚报fixture为真实demo。
- [ ] **Step 5:** 归档供F8/D2复用的fixture V1/V2生成物和Parser向量，断言AOT固定Envelope hash不变；本Task以Managed互操作和真实Tok demo结果验收，不等待D2。真实IL2CPP热更新类型验证由F8/D2负责并单独记录。提交 `test(protocol): 验证Tok互操作与热更生成类型`。

## 明确后置的会话能力

后续Task另立计划实现：ACK连续应用位置、Notify顺序与去重、ResumeRequest/Response、RecoveryComplete门禁、token刷新、重连最大request_id、业务未知结果处置和生产加密认证。首期UI只显示已有的连接/认证能力，不能显示“无缝重连已完成”。
