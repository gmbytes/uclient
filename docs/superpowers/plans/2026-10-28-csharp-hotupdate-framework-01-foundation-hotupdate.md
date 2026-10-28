# 基础框架与 C# 热更新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可复现的 AOT/热更边界，先证明最小 C# DLL 更新，再完成签名发布、完整 slot、试启动、回退和两平台 IL2CPP 验证。

**Architecture:** UGF 提供基础模块与原生 Package 资源加载，AOT 协调发布事务并一次加载热更程序集。每个 release 是不可变完整目录；资源根在 ResourceComponent.Start 前注入，热更失败下次完整启动回退。

**Tech Stack:** Unity 2022.3.25f1、官方 GameFramework/UGF、HybridCLR 9.0.0 社区版、C#、IL2CPP、uGUI、Newtonsoft JSON、RSA/SHA-256。

**Spec:** [设计基线](../specs/2026-10-28-csharp-hotupdate-framework-design.md)；[总计划](2026-10-28-csharp-hotupdate-framework.md)。本文件所有 Task 初始均未完成。

## Global Constraints

- 继承总计划全部 Global Constraints；工作目录 `E:/sgame/client`，旧工程只读。
- Android ARM64 与 Windows x64 首期；iOS 不做首期验收。
- GF Package 模式、完整版本快照、无 Addressables、无 Lua、无业务迁移。
- AOT 不引用 Hotfix；同一进程一个资源根和一个热更程序集集合。
- 签名私钥不入库；元数据使用原 PlayerBuildId 和对应平台归档。
- “通过”必须归档命令、退出码、测试 XML/JSON 与相关 Player 日志，不能只改 checkbox。

## Review Focus

1. ResourceComponent 初始化时覆盖自定义 slot：F2 检验真实读根和迟到设置拒绝。
2. 半套候选、磁盘不足、原子状态文件中断：F5 在每个持久化边界注入失败。
3. 错 Player/平台/元数据：F4/F6 在加载前拒绝，DllLoadCount=0。
4. 加载异常后假回滚：F7 确认同进程不再加载，下一进程选择 known-good。
5. Editor 可运行而 Release IL2CPP 不可运行：F3 最小 Player 门槛、F8 两平台门槛。

## 通用验证与提交约定

路径简写规则：`Framework/`和`Editor/`相对`Assets/Scripts/`；`Tests/EditMode/`和`Tests/PlayMode/`相对`Assets/`；`Tests/Fixtures/`相对客户端根。其他路径相对`E:/sgame/client`。目录权威规范为[工程目录规划](../../project-layout.md)。

F1 建立 `Tool/Client/Test-Client.ps1 -Task <ID> -Mode EditMode|PlayMode`，以 NUnit `Category("<ID>")` 选择测试；无选中测试、失败或测试进程异常均退出非零。结果落 `Builds/Tests/<ID>/<runId>/results.xml`，成功摘要为 `TASK_PASS <ID>`。Player 验证使用各 Task 指定的独立脚本，不能伪造 XML。

每个 Task 的验证通过后，检查实际差异，仅提交该 Task 文件与 Unity 生成 `.meta`，使用 Conventional Commits 和中文说明；提交动作属于后续实施，不在本轮执行。

### Task F1：工程、依赖锁与可执行测试入口

**Files:**
- Create: `ProjectSettings/ProjectVersion.txt`、`Packages/manifest.json`、`Packages/packages-lock.json`、`.gitignore`、`.editorconfig`、`.gitattributes`。
- Create: `Assets/Scenes/Bootstrap.unity`、`Assets/Prefabs/App/GameFramework.prefab`。
- Create: `Assets/Scripts/Framework/Client.Framework.asmdef`、`Assets/Scripts/Editor/Client.Editor.asmdef`、`Assets/Tests/EditMode/Client.EditMode.Tests.asmdef`、`Assets/Tests/PlayMode/Client.PlayMode.Tests.asmdef`。
- Create: `Tool/Client/Test-Client.ps1`、`Tool/Client/Smoke-Player.ps1`、`Assets/Scripts/Editor/Build/SmokeBuild.cs`。
- Create: `docs/dependencies.lock.json`、`docs/third-party-notices.md`、`Assets/Tests/PlayMode/BootstrapSmokeTests.cs`。

**Interfaces:** `SmokeBuild.Run()` 从参数读取 Android/Windows64 与全新输出目录；`Test-Client.ps1 -Task F1 -Mode PlayMode` 是后续统一测试入口。依赖锁记录 Unity 精确版本、UGF 官方提交及所带 GF 库 hash、HybridCLR 9.0.0 tag/Installer源码标识、官方 UI/TMP/JSON/TestFramework 包解析版本；不使用浮动 git branch。

- [x] **Step 1:** 创建最小 Unity 项目和测试运行器的必要配置，按目录规范把自研代码放Assets/Scripts；官方UGF源码快照放Assets/ThirdParty/GameFramework，读取官方版本并写不可变锁，保留许可；HybridCLR和官方Unity包仍用UPM依赖。不复制旧Assets；Protobuf依赖安装留给N1。
- [x] **Step 2:** 写 `BootstrapCreatesOneFrameworkHost`：加载启动场景后断言根宿主数量 `1`、Base/Event/Download 各一个；无初始化异常。运行 F1，确认缺少宿主时失败。
- [x] **Step 3:** 制作只含 AOT 基础组件的 GameFramework；Resource/UI 不在此时初始化。配置 Unity 2022.3.25f1、Android ARM64/Windows x64 IL2CPP、.NET Standard 2.1；安装对应工具链和 HybridCLR。
- [x] **Step 4:** 运行 `./Tool/Client/Test-Client.ps1 -Task F1 -Mode PlayMode`，预期 `TASK_PASS F1`。分别运行 `./Tool/Client/Smoke-Player.ps1 -Platform Windows64`、`./Tool/Client/Smoke-Player.ps1 -Platform Android`，预期编译退出0且生成平台产物；记录工具链缺失为阻塞，不能用 Mono 顶替。
- [x] **Step 5:** 提交 `chore(client): 建立可复现Unity与测试基线`。本 Task 不证明热更成功。


F1验收：1f6cdc1/0799efc；EditMode5/5、PlayMode1/1、双平台IL2CPP实际编译通过、零测试拒绝，独立规范与质量审查通过；详见实施进度。

### Task F2：原生 GF 完整资源 slot 原型

**Files:**
- Modify: `Assets/ThirdParty/GameFramework/Scripts/Runtime/Resource/ResourceComponent.cs`，只加通用初始化前路径注入。
- Create: `docs/vendor-patches/ugf-resource-paths.md`、`Assets/Prefabs/App/GameRoot.prefab`。
- Create: `Assets/Scripts/Framework/Resource/ResourceSlotBootstrap.cs`、`Framework/Resource/ResourceService.cs`。
- Create: `Assets/Scripts/Editor/Build/ResourceSlotProofBuilder.cs`、`Tests/PlayMode/ResourceSlotTests.cs`、`Tool/Client/Test-ResourceSlots.ps1`。

**Interfaces:** UGF 增加 `void SetCustomResourcePaths(string readOnlyPath, string readWritePath)`，只能在 Start 前调用，Start 后抛 `InvalidOperationException`，未设置时保持官方默认。项目 `ResourceSlotBootstrap.Activate(string slotRoot)` 在创建/激活 GameRoot 前注入 `<slot>/Resources` 和 `<slot>/Scratch`，固定 Package 模式。原生 `IResourceManager`、版本表和 Bundle 格式不改。

- [x] **Step 1:** 写 `ResourceRootIsInjectedBeforeInitialization`、`LatePathMutationIsRejected`：断言记录顺序为 SetPaths→SetResourceMode→AddAgents→InitResources；迟到修改抛异常且 ReadOnlyPath 不变。
- [x] **Step 2:** 运行 `./Tool/Client/Test-Client.ps1 -Task F2 -Mode PlayMode`，确认原生路径覆盖/迟到注入用例失败。
- [x] **Step 3:** 实现单一通用 UGF 补丁、项目适配和延迟 GameRoot；制作 A/B 两套同地址不同标记的原生 Package 测试资源。Bootstrap AOT UI 不依赖这些资源。
- [x] **Step 4:** 测试通过后运行 `./Tool/Client/Test-ResourceSlots.ps1 -Platform Windows64`，同一 Player 冷启动两次分别读 A/B，预期 `RESOURCE_SLOT_PASS A B`；记录资源根、版本表 hash、加载标记，确认没有跨 slot 文件写入。
- [x] **Step 5:** 提交 `feat(resources): 支持初始化前选择完整资源版本`。若固定版 GF 无法完成此原型，停在此 Task 修订适配，不继续假设后续回滚成立。


F2验收：9a2982c，原生Windows Package同Player A/B自然退出0、资源快照不变、PlayMode3/3及普通Player隔离1/1，独立规范/质量审查通过；Android及完整热更新仍待后续Task。

### Task F3：稳定接口、启动宿主与最小 C# 热更证明

**Files:**
- Create: `Assets/Scripts/Framework/Contracts/Client.Contracts.asmdef`、`IClientContext.cs`、`IHotfixEntry.cs`、`IResourceService.cs`、`IUIService.cs`、`ISessionClient.cs`、`SessionContracts.cs`、`ILogService.cs`。
- Create: `Assets/Scripts/Framework/Bootstrap/ClientBootstrap.cs`、`UIUpdate.cs`、`Client.Bootstrap.asmdef`。
- Create: `Assets/Scripts/Game/Client.Game.asmdef`、`Assets/Scripts/Game/Protocol/Client.Game.Protocol.asmdef`。
- Create: `Assets/Scripts/Framework/HotUpdate/HotfixAssemblyLoader.cs`、`Tests/EditMode/AssemblyBoundaryTests.cs`、`Tests/Fixtures/HotfixProof`、`Tool/Client/Test-MinimalHotfix.ps1`。

**Interfaces:**
- `IHotfixEntry.InitializeAsync(IClientContext context, CancellationToken token): Task`；`ShutdownAsync(CancellationToken token): Task`。
- `IClientContext` 暴露 `Resources:IResourceService`、`UI:IUIService`、`Session:ISessionClient`、`Log:ILogService`、`Lifetime:CancellationToken`、`void ReportReady()`。
- `ILogService.Write(ClientLogLevel level,string code,string message):void`；ClientLogLevel固定Info/Warning/Error，凭据/token不作为日志参数。
- `IResourceService.LoadAsync<T>(string assetName,CancellationToken token): Task<IAssetHandle<T>> where T:UnityEngine.Object`；`IAssetHandle<T>:IDisposable` 提供 `T Asset`。
- `IUIService.OpenAsync(string viewId,CancellationToken token):Task<IViewHandle>`；`IViewHandle` 提供 `GameObject Root` 与幂等 `Close()`。
- `ISessionClient.LoginAsync(LoginInput input,CancellationToken token):Task<LoginResult>`、`Disconnect()`、`event Action<SessionState> StateChanged`。`LoginInput(string credential,string resumeToken)` 恰好一个非空。`LoginResult` 含 `Succeeded:bool`、`Error:LoginError`、`ServerErrorCode:uint32`、`Identity:SessionIdentity`、`SafeMessage:string`；Identity含PlayerId/SessionId字符串、Epoch:uint64、ResumeToken字符串与ResumeTokenTtlMs:uint64。`SessionState` 为 Idle/Connecting/Authenticating/Authenticated/Disconnected；LoginError为None/InvalidInput/Busy/ConnectFailed/Timeout/Cancelled/Rejected/ProtocolError/Disconnected。不把凭据和token写日志。
- `HotfixAssemblyLoader.LoadOnce(IReadOnlyList<VerifiedAssembly> assemblies):IHotfixEntry`；VerifiedAssembly含Name/Bytes/Sha256/Dependencies，只接受已校验字节与顺序，第二次调用拒绝。

- [x] **Step 1:** 写`AotAssembliesNeverReferenceGame`，遍历已编译AOT引用断言无Client.Game*；写`FrameworkNeverReferencesBootstrap`防止嵌套目录引入反向依赖；写`EntryIsLoadedOnce`，断言重复加载请求被拒绝。运行F3 EditMode确认红灯。
- [x] **Step 2:** 建立 asmdef 单向引用和无业务契约，Hotfix autoReferenced=false，配置 HybridCLR 热更列表。入口仅通过稳定接口和一个配置的类型全名定位，不用 AOT `typeof(HotfixType)`。
- [x] **Step 3:** 做独立测试 fixture：v1 Entry 返回标记 `proof-v1`；v2 新增纯 C# 类型/分支返回 `proof-v2:42`。只在 Test build 的 Proof 场景从本地测试目录加载，不给产品启动器加无签名远程旁路。
- [x] **Step 4:** 运行 `./Tool/Client/Test-Client.ps1 -Task F3 -Mode EditMode`；运行 `./Tool/Client/Test-MinimalHotfix.ps1 -Platform Windows64`。预期同一个 IL2CPP Player hash 不变，第二次启动日志出现 `HOTFIX_PROOF proof-v2:42`，v1分支不执行。该 Task 的元数据仍须来自这次测试 Player。
- [x] **Step 5:** 提交 `feat(hotfix): 建立稳定边界并验证最小CSharp更新`。这是第一个代码热更闭环，远程更新和回滚尚未完成。


F3验收：7b58cec，11/11边界测试、实际Windows同Player v1/v2新增类型42执行、48份原Player元数据、原生完整哈希不变及普通Player隔离通过；独立规范/质量审查通过。Protocol实际接入和生产签名/下载/回退仍在后续。

### Task F4：签名清单与兼容门禁

**Files:**
- Create: `Framework/Release/ReleaseManifest.cs`、`ChannelPointer.cs`、`ManifestVerifier.cs`、`ReleaseCompatibility.cs`、`RelativePathPolicy.cs`（Framework路径均位于`Assets/Scripts`）。
- Create: `Tests/EditMode/ReleaseManifestTests.cs`、`Tests/Fixtures/Releases/ManifestCases`、`Editor/Build/ReleaseManifestWriter.cs`。

**Interfaces:** `ManifestVerifier.Verify(byte[] json,byte[] signature,PlayerIdentity player):VerifiedRelease`；`VerifyChannel(byte[] json,byte[] signature):VerifiedChannelPointer`，渠道指针含ReleaseId/ManifestRelativePath/ManifestSha256/KeyId。失败抛带有限 `ReleaseErrorCode` 的异常。`PlayerIdentity` 固定 playerBuildId/platform/architecture/unityVersion/contractVersion/envelopeSha256/aotMetadataSetId。`VerifiedRelease` 是后续下载唯一输入，包含不可变文件集合和程序集顺序。渠道和release均对原始字节签名，渠道路径也受相对路径策略约束。

- [x] **Step 1:** 写 `WrongIdentityLoadsZeroDlls`：分别篡改上述身份字段，断言兼容拒绝且加载数0；写 `SignatureCoversOriginalBytes`：清单/渠道任一字节改变即失败；写 `ManifestRejectsUnsafePaths` 覆盖绝对路径、`..`、重复路径、重复JSON键、超过256KiB；渠道指定的release hash不匹配必须拒绝。
- [x] **Step 2:** 运行 `./Tool/Client/Test-Client.ps1 -Task F4 -Mode EditMode`，确认无校验实现时失败。
- [x] **Step 3:** 使用 Newtonsoft JSON 严格解析、RSA-2048/SHA-256 分离签名和 SHA-256；先验签原始字节再解析，不用重序列化结果验签。KeyId只能选择内置可信公钥，未知KeyId拒绝。测试运行时在忽略的Builds/TestKeys生成临时密钥，fixtures只保留非秘密向量/公钥；正式builder拒绝测试KeyId，任何签名私钥不入库。固定文件种类为 NativeResource、HotfixDll、AotMetadata、Configuration。
- [x] **Step 4:** 重跑 F4，预期所有有效/无效矩阵通过；有效清单输出 `VerifiedRelease`，异常不泄露路径外内容或凭据。
- [x] **Step 5:** 提交 `feat(release): 校验发布签名与Player兼容性`。

### Task F5：下载、物化完整 slot 与原子状态存储

**Files:**
- Create: `Framework/Release/ReleaseDownloader.cs`、`ReleaseStore.cs`、`ReleaseStateStore.cs`、`BaseReleaseInstaller.cs`、`DiskBudget.cs`。
- Create: `Framework/Release/GfDownloadTransport.cs`、`Tests/EditMode/ReleaseTransactionTests.cs`、`Tests/PlayMode/BaseReleaseInstallTests.cs`。

**Interfaces:** `ReleaseStore.PrepareAsync(VerifiedRelease release,IProgress<ReleaseProgress> progress,CancellationToken token):Task<PreparedRelease>`；`PreparedRelease` 只指完整目录。`ReleaseStateStore.Read():ReleaseState`、`WriteAtomic(ReleaseState state):void`；`ReleaseState` 保存 KnownGood、PreviousGood、Candidate、Attempt、BadReleaseIds。`IFileFaultInjector.Hit(string checkpoint)` 仅测试注入，发布默认空实现。

- [x] **Step 1:** 写 `InterruptedPreparePreservesKnownGood`：在下载/拷贝/hash/改名/写状态每个边界失败，断言旧known-good不变且候选不会返回prepared；写 `ChangedOnlyDownloadStillCreatesFullSnapshot`：两文件中仅一文件下载，但新slot两文件齐全且hash正确。
- [x] **Step 2:** 写 `NoSpaceAndCancellationNeverActivate`、`AndroidBaselineUsesStreamReader`、`StateRecoveryUsesValidPrimaryOrBackup`；运行 F5 EditMode/PlayMode 确认红灯。
- [x] **Step 3:** 实现并发2、单文件最多3次传输尝试（退避1/2秒，取消/校验/兼容错误不重试）的 UGF Download 适配；`.partial` 暂存，同卷改名。准备前检查“待物化完整slot+临时文件+64MiB余量”，不足返回NoSpace；禁止写稳定slot。断点续传最终仍整文件hash校验。
- [x] **Step 4:** 包内基线在 Android 用平台读取流物化；复用旧文件先hash再复制，不使用可变共享或硬链接。状态使用同目录临时文件+替换/备份，启动恢复仅接受结构及引用有效的一份，所有错误保留足够诊断。
- [x] **Step 5:** 运行 `./Tool/Client/Test-Client.ps1 -Task F5 -Mode EditMode` 与 `./Tool/Client/Test-Client.ps1 -Task F5 -Mode PlayMode`，预期 `TASK_PASS F5`，无临时文件被解析为活跃资源。提交 `feat(release): 原子准备完整版本目录`。

### Task F6：生产加载器、AOT 补充元数据与裁剪门禁

**Depends on:** F5、N1（Google.Protobuf固定运行库及信封已到位；不要求N2/N3）。

**Files:**
- Modify: `Framework/HotUpdate/HotfixAssemblyLoader.cs`、`Assets/Scripts/Framework/Bootstrap/ClientBootstrap.cs`。
- Create: `Framework/HotUpdate/AotMetadataLoader.cs`、`HotfixLoadPlan.cs`、`Editor/Build/PlayerBaselineArchive.cs`、`Editor/Build/HotfixCompatibilityCheck.cs`。
- Create: `Assets/Scripts/Framework/Bootstrap/link.xml`、`Tests/EditMode/HotfixLoadPlanTests.cs`、`Tests/Fixtures/HotfixAotProbe`。

**Interfaces:** `AotMetadataLoader.Load(ArchivedMetadataSet set):void`；每次 `RuntimeApi.LoadMetadataForAOTAssembly` 检查返回码；只读匹配 platform/playerBuildId。`HotfixCompatibilityCheck.Check(string playerArchive,string compiledHotfixRoot):CompatibilityReport`，输出缺失AOT引用、程序集依赖/顺序及是否需要新Player。

- [x] **Step 1:** 写 `RejectsMetadataFromAnotherPlatform`、`MissingAotApiRequiresNewPlayer`、`AssemblyOrderIsTopologicalAndComplete`，断言错集/缺DLL/循环/未预留程序集名在加载前失败；运行 F6 EditMode 确认失败。
- [x] **Step 2:** 实现正式启动只接受F4/F5的Verified/Prepared产物。按元数据→Client.Game.Protocol→Client.Game顺序加载，移除产品对F3 Proof fixture的所有引用。
- [x] **Step 3:** 主包构建先编译热DLL、生成link/桥接/泛型引用和目标AOT输出，封装基线，再构建Player；将最终post-strip输出与用于包内基线的元数据逐文件比对，不一致使构建失败并重建基线。归档不可变PlayerBuildId、依赖锁、裁剪配置、平台、元数据hash和原生产物hash。
- [x] **Step 4:** 补丁构建只从选定Player归档取元数据，使用锁定HybridCLR的 MissingMetadataChecker 或同等官方检查入口；新增未保留AOT API、库升级或稳定契约改变返回RequiresNewPlayer。
- [x] **Step 5:** 跑 F6 EditMode，并用 Release IL2CPP probe 覆盖 `List<热更struct>`、`Dictionary<uint,IMessage>`、async/Task状态机、Protobuf RepeatedField/MapField、注册Parser与未知字段；任一失败不能发布。提交 `feat(hotfix): 绑定AOT归档并拦截不兼容补丁`。

### Task F7：启动状态机、试运行提交与下进程回退

**Files:**
- Create: `Framework/Release/ReleaseCoordinator.cs`、`TrialLaunchPolicy.cs`、`Framework/Release/ReleaseDiagnostics.cs`。
- Modify: `Assets/Scripts/Framework/Bootstrap/ClientBootstrap.cs`、`UIUpdate.cs`。
- Create: `Tests/EditMode/TrialLaunchTests.cs`、`Tests/PlayMode/BootstrapLifecycleTests.cs`。

**Interfaces:** `ReleaseCoordinator.PrepareLaunchAsync(CancellationToken token):Task<LaunchSelection>`；`BeginTrial(LaunchSelection selection):void` 在资源初始化前持久化；实际提交只接受绑定当前selection/context/epoch的Ready请求，稳定`IClientContext.ReportReady()`由该捕获上下文转发。兼容形状`ReportReady(string releaseId):void`缺少调用者代次，必须有限拒绝且不得推断当前上下文提交。`FailCurrent(ReleaseErrorCode code):void`标记坏候选并进入RestartRequired。一个未完成trial下次启动即排除候选并回退，不无限循环尝试。

- [x] **Step 1:** 写 `UnfinishedTrialRollsBackOnNextProcess`：重建协调器后隔离未完成的Candidate C，选择仍有效的KnownGood K并保留PreviousGood P；`FailedKnownGoodPromotesSurvivingPreviousAtomically`另验证KnownGood自身失败时原子转向PreviousGood。写 `ServerLoginFailureDoesNotBlacklistRelease`：网络/认证失败不改变known-good；写 `StaleReadyCannotCommitAnotherRelease`。
- [x] **Step 2:** 运行 F7 EditMode，确认默认状态机无法满足上述断言。
- [x] **Step 3:** 实现 Check→Prepare→Select→Trial→ResourceInit→Metadata→Hotfix→LoginReady；Check先下载并验签channel.json/channel.sig，再按指定hash读取并验签release清单；使用F4结果驱动F5。启动场景AOT UI显示有限状态、进度、重试/退出。准备阶段失败保留旧版本；Resource开始初始化后失败进入RestartRequired，禁止同进程再次切根/Assembly.Load。
- [x] **Step 4:** `ReportReady` 必须由本地资源加载、热入口、Protobuf自检和登录首帧共同触发；服务器不可用不能阻止本地版本成为known-good。取消/退出清理下载和监听，旧代次回调无效；只清理不被任何状态引用的版本，保留至少当前和上一可用版本。
- [x] **Step 5:** 运行 `./Tool/Client/Test-Client.ps1 -Task F7 -Mode EditMode` 与 PlayMode，预期全过。提交 `feat(release): 支持试启动与重启后回退`。

### Task F8：Android/Windows 核心热更新门槛

**Files:**
- Create: `Tool/Client/Test-HotUpdate.ps1`、`Tests/Fixtures/HotfixVersions/V1`、`V2`、`ThrowOnStart`。
- Create: `Assets/Scripts/Editor/Build/HotUpdateProofBuild.cs`、`docs/verification/hotupdate.md`。

**Interfaces:** `Test-HotUpdate.ps1 -Platform Android|Windows64 -DeviceId <Android序列号>` 生成独立buildRun目录，输出 `hotupdate-result.json`；字段包括PlayerBuildId、APK/EXE/native hash、v1/v2 release hash、实执行标记、平台、设备、各步骤结果、日志路径。不得把未连接Android设备记通过。

- [ ] **Step 1:** 固定失败断言：没有安装v1基线、Player hash改变、仅文本资源变化、v2 C#新分支未执行、热DLL被编入AOT、元数据probe失败，任一情况脚本非零退出。
- [ ] **Step 2:** 在 Windows x64 Release IL2CPP 中，安装/启动v1→只发布v2资源与DLL→完整重启→出现新增类型/方法的 `HOTFIX_V2_EXECUTED 42`；确认Native主包hash完全相同，v2资源标记也改变。
- [ ] **Step 3:** 在用户选择的雷电Android环境按同流程运行ARM64 APK，不重新安装；记录设备/系统/API、主ABI、转译器、APK/process ABI与签名hash。进入后台、强杀、无网重启验证known-good；真实ARM硬件结果单列NotRun，不混同模拟器结果。
- [ ] **Step 4:** 发布ThrowOnStart版，确认本进程不二次加载；手工/脚本重新启动后回到v2 known-good；随后关闭网络仍能启动。预期两平台分别输出 `HOTUPDATE_PASS`，JSON每项均有证据。
- [ ] **Step 5:** 审查并提交 `test(hotfix): 验证两平台同主包代码更新与回退`。F8未通过不得声明框架已具备可交付热更新。
