# 横竖屏 UI、登录示例与交付 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付可热更的 C# 登录示例、统一 UI 生命周期与横竖屏适配，并以 Android/Windows 同主包发布和故障矩阵验收框架。

**Architecture:** AOT 层持有屏幕快照、uGUI宿主与稳定Bridge；热更层持有登录Presenter及页面状态。资源发布沿用F系列slot，通信沿用N系列；最终C#构建服务统一生成Player基线与兼容补丁。

**Tech Stack:** uGUI、TextMeshPro、GameFramework UI、HybridCLR、Google.Protobuf、Unity Test Framework、IL2CPP、C#构建服务。

**Spec:** [设计基线](../specs/2026-10-28-csharp-hotupdate-framework-design.md)；[总计划](2026-10-28-csharp-hotupdate-framework.md)；[UI编辑规范](../../ui-authoring.md)。U1–U3在F3后可并行于N系列；U4依赖U3，U5依赖U4/N3，D1依赖F8/N4/U5。

## Global Constraints

- 继承总计划全部约束；首期无具体游戏业务、无完整美术/旧UI搬运。
- 默认竖屏；UI按实际宽高适配横屏，不仅缩放；Windows默认720×1280客户区可自由调整。
- Portrait参考1080×1920，Landscape参考1920×1080，CanvasScaler.matchWidthOrHeight=0.5。
- 同一个登录View和Presenter贯穿旋转；不清空输入、焦点或在途请求。
- 背景满屏，交互处于安全区；手机键盘与极短窗口提供可滚动到达路径。
- 真实测试账号只用于开发Tok demo；模拟模式明确标识，不实现生产认证。
- Android ARM64 APK在用户选择的雷电环境、以及Windows x64 Release IL2CPP都必须验证；记录转译环境限制，真实ARM硬件和iOS首期保持未验证。
- 简写Framework/、Editor/相对Assets/Scripts；Tests/EditMode/、Tests/PlayMode/相对Assets；其余相对客户端根。以[工程目录规划](../../project-layout.md)为准。
- 制作工作流参考旧工程模板/配置/显式绑定/校验，落地为全C#编辑器；具体规则以UI编辑规范为准。

## Review Focus

1. 刘海、打孔、折叠/窗口变化、安全区瞬间为零：U1/U2验证夹取和有效快照。
2. 键盘打开时旋转与长文本：U2/U5验证焦点、可滚动到达和请求状态保留。
3. 打开/关闭/重用窗口造成重复按钮回调：U3/U5验证订阅数和一次请求。
4. 构建入口结果路径、源码修改和同时出包：D1验证统一BuildResult与项目互斥。
5. 未完成下载/坏补丁/强杀后还显示成功：D2验证实际Player、版本标记和下进程恢复。

### Task U1：统一屏幕快照与布局模式

**Files:**
- Create: `Framework/UI/ScreenMetrics.cs`、`ScreenLayoutSnapshot.cs`、`ScreenLayoutService.cs`、`ScreenLayoutComponent.cs`。
- Create: `Tests/EditMode/ScreenLayoutTests.cs`、`Tests/PlayMode/ScreenChangeTests.cs`。

**Interfaces:** `ScreenLayoutService.Evaluate(ScreenMetrics metrics):ScreenLayoutSnapshot`；快照含Width/Height、ClampedSafeArea、LayoutMode(Portrait/Landscape)、Revision。`event Action<ScreenLayoutSnapshot> Changed` 同帧最多一次，同内容不重复。组件从Unity Screen读取，服务计算可注入测试。

- [x] **Step 1:** 写参数化用例：1080×1920→Portrait、1920×1080→Landscape、1080×1080→Landscape；无效0尺寸保持上一有效快照；越界安全区夹到客户区，空安全区回退全客户区。
- [x] **Step 2:** 写 `SameMetricsPublishNoEvent`、`SameFrameChangesPublishOnce`、`OneValidResizeAdvancesOneRevision`；运行 `./Tool/Client/Test-Client.ps1 -Task U1 -Mode EditMode` 确认红灯。
- [x] **Step 3:** 实现浮点宽高比计算和尺寸/安全区/方向检测，变化后在LateUpdate统一发布；不按旧机型名分支，不每帧重建UI，不使用整数除法。
- [x] **Step 4:** 运行U1 EditMode与 `./Tool/Client/Test-Client.ps1 -Task U1 -Mode PlayMode`；尺寸稳定后2帧内可观察到新快照，其他帧不重复回调。
- [x] **Step 5:** 提交 `feat(ui): 统一屏幕与安全区变化模型`。

### Task U2：双布局、安全区与键盘可见性

**Files:**
- Create: `Framework/UI/AdaptiveCanvas.cs`、`SafeAreaRoot.cs`、`AdaptiveLayout.cs`、`AdaptiveLayoutDefinition.cs`、`KeyboardVisibilityController.cs`。
- Create: `Assets/Content/UI/Common/AdaptiveUIRoot.prefab`、`Assets/Content/UI/Login/UILogin.Layout.asset`。
- Create: `Tests/PlayMode/AdaptiveLayoutTests.cs`、`KeyboardLayoutTests.cs`、`Tool/Client/Test-UIMatrix.ps1`。

**Interfaces:** `AdaptiveLayout.Apply(ScreenLayoutSnapshot snapshot):void`；`AdaptiveLayoutDefinition` 包含Portrait/Landscape的参考分辨率、锚点、边距和表单最大宽度；`SafeAreaRoot.Apply(Rect safeArea,Vector2 screenSize):void`；键盘控制器对已有ScrollRect调整可见范围，不销毁/新建输入框。

- [x] **Step 1:** 写 `InteractiveRectInsideSafeArea`（允许1物理像素误差）、`LayoutUsesCorrectReferenceResolution`、`RotatingPreservesViewAndInputInstance`（InstanceID保持）和 `ShortWindowCanScrollToSubmit`，运行U2 PlayMode确认红灯。
- [x] **Step 2:** 制作统一UI根的CanvasScaler，ScreenMatchMode=MatchWidthOrHeight、match=0.5，竖/横参考分辨率按规范切换，首期ScreenSpaceOverlay；窗口子Canvas不再各挂Scaler。Bootstrap UIUpdate独立AOT画布复用此策略，GameRoot就绪后关闭；正式场景只保留一个EventSystem。制作背景层和SafeAreaRoot；竖屏品牌区在上、横屏品牌区左侧约35%；表单宽≤760、边距48、按钮高≥96参考像素，高度不足启用ScrollRect。
- [x] **Step 3:** 使用同一套控件和状态切布局；只改锚点、布局参数和容器顺序，每次快照至多一次布局重建；不得通过停用输入控件清掉焦点/编辑状态。键盘区域有效时滚动焦点输入/主按钮可见，无有效区域时保留用户可滚动表单。
- [x] **Step 4:** 运行 `./Tool/Client/Test-Client.ps1 -Task U2 -Mode PlayMode`；运行 `./Tool/Client/Test-UIMatrix.ps1 -Profile Editor`，覆盖720×1280、1080×1920、1080×2400、1536×2048、1280×720、1920×1080、2560×1080、720×480及四边模拟安全区，输出截图和几何断言，不能只靠截图主观判断。
- [x] **Step 5:** 提交`feat(ui): 支持双布局与安全区键盘适配`。Android软键盘与旋转留D2在雷电验收，不把Editor或模拟器结果写成真机通过。

### Task U3：GF窗口宿主与C#热更绑定生命周期

**Files:**
- Create: `Framework/UI/UIService.cs`、`UIViewBridge.cs`、`UIBindings.cs`、`UIViewHandle.cs`、`UIViewDefinition.cs`、`UIGroups.cs`。
- Create: `Assets/Content/UI/Common/UIViewDefinitions.asset`、`Editor/UI/UIBindingValidator.cs`。
- Create: `Tests/PlayMode/UILifecycleTests.cs`、`Tests/EditMode/UIBindingTests.cs`。

**Interfaces:** `UIService:IUIService`使用F3 OpenAsync/Close契约；`UIViewBridge:UIFormLogic`只用于主窗口，序列化RootCanvas:Canvas、RootCanvasGroup:CanvasGroup、Bindings:UIBindings，提供可见性/关闭通知和`Component Resolve(string bindingId)`。通用AOT `UIBindings:MonoBehaviour`持显式单项/数组引用表，提供`Component Resolve(string id)`与`IReadOnlyList<Component> ResolveMany(string id)`；UIViewBridge委托单项Resolve，子组件只挂UIBindings。UIViewDefinition包含ViewID/PrefabAddress/Group/Modal/CloseOnMask/LayoutDefinition。UIGroups固定Background/Page/Popup/Loading/System。Open取消后迟到资源必须释放，Close幂等；Bridge.Awake不启动资源/网络流程，独立预览不依赖Game.Entry。

- [x] **Step 1:** 写 `OpenCloseReuseHasOneSubscription`：连续开关20次，活动监听数回基线、点击只触发一次；`CancelledOpenReleasesLateAsset`；`ClosedWindowIgnoresLateCallback`；`PopupOrderingAndInputBlockRemainCorrect`。
- [x] **Step 2:** 运行U3 PlayMode确认缺少生命周期处理时失败。
- [x] **Step 3:** 通过GF UI组和UIForm生命周期实现句柄；热更Presenter接收句柄并自行绑定/解绑，不把热更MonoBehaviour写进启动场景或Prefab。UI Prefab由GF UI模块及其原生Resource helper唯一加载/释放，UIViewHandle只持UIForm生命周期，不先经IResourceService重复预加载同一Prefab；其他显式资源使用IAssetHandle管理。
- [x] **Step 4:** 编辑器绑定校验检查必要引用、类型、重复ID和缺失字体；构建时失败阻止发布。运行 `./Tool/Client/Test-Client.ps1 -Task U3 -Mode EditMode` 和PlayMode，预期 `TASK_PASS U3`。
- [x] **Step 5:** 提交 `feat(ui): 建立可热更窗口绑定与生命周期`。

### Task U4：UI制作模板、配置与C#绑定工具

**Files:**
- Create: `Editor/UI/UIWindowCreator.cs`、`UIConfigWindow.cs`、`UIBindingsEditor.cs`、`UIBindingGenerator.cs`、`UIAuthoringValidator.cs`、`UIPreviewWindow.cs`。
- Create: `Assets/Content/UI/Common/UIWindowTemplate.prefab`、`Assets/Scenes/UIEdit.unity`。
- Create: `Tests/EditMode/UIAuthoringTests.cs`、`UIBindingGeneratorTests.cs`、`Tests/Fixtures/UIBindings/Expected`。
- Reuse: U3的UIBindingValidator、UIViewDefinition、UIViewBridge、UIBindings和U2布局资产；不复制Lua工具运行依赖。

**Interfaces:** `UIWindowCreator.Create(string windowName,string contentDirectory):UIViewDefinition`，windowName使用UILogin等完整UI名；`UIBindingGenerator.Generate(UIBindings source,string namespaceName,string className,string outputFile):BindingGenerationResult`；`UIAuthoringValidator.Validate(UIViewDefinition definition):UIValidationReport`。错误包含稳定Code、AssetPath、ObjectPath、Message、Severity。输出必须为当前Game/<功能>/Generated/<className>.G.Bindings.cs；className、Prefab根名及ViewID一致。生成partial与手写文件同namespace/程序集，手写文件不覆盖，失败不改已有生成物。

- [x] **Step 1:** 写`CreatesWindowWithSeparateCanvasAndDefinition`、`RejectsWindowOwnedEventSystem`、`RejectsDuplicateViewAndBindingIds`、`RejectsOutOfRootOrWrongTypeReference`、`NestedWidgetHasNoWindowBridge`、`GeneratorPreservesHandwrittenPartial`、`SecondGenerationHasNoDiff`、`SameUINameAcrossCodePrefabAndConfig`、`GeneratedAndHandwrittenCompileAsOnePartialType`；非法UI名/输出后缀、缺引用或非法字段名生成失败且旧文件hash不变。
- [x] **Step 2:** 运行`./Tool/Client/Test-Client.ps1 -Task U4 -Mode EditMode`，确认缺工具或不合规样例时失败。
- [x] **Step 3:** 实现Game/UI/Create Window和Config：校验UI{Name}命名，创建同名Prefab根、手写UI{Name}.cs骨架、UI{Name}.Config/Layout/Text.asset；按模板建UINode/Background/SafeArea/Static/Dynamic/PoolNode，根挂UIViewBridge+UIBindings，Canvas/CanvasGroup/Raycaster在UINode。已有资产/代码拒绝覆盖，修改可Undo并保存；ViewID与UI名一致，公共组件保留嵌套Prefab。
- [x] **Step 4:** 实现UIBindings Inspector拖拽、类型选择、对象定位和数组重排；生成同一UI{Name} partial类型的强类型引用、BindComponents(UIBindings)和ClearBindings，写UI{Name}.G.Bindings.cs并标auto-generated，不生成业务/事件方法。节点名只作首次ID建议，运行时按显式引用Resolve；UI统一大写。
- [x] **Step 5:** 实现Validate Selected和Preview：复用绑定校验，增加规范中的主/子窗口、Canvas/Scaler/Raycaster、Missing脚本、TMP字体和公共Prefab检查。打开独立UIEdit场景做布局预览，无更新/服务器流程；原编辑内容需先由用户正常保存，不强制丢弃。输出错误/警告和对象定位；测试资产写忽略的Assets/Tests/Generated/<runId>并安全清理。
- [x] **Step 6:** 重跑U4 EditMode，预期TASK_PASS U4；用生成模板完成一次“新建→登记→拖引用→生成→编译→校验→横竖预览”，记录产物路径和生成前后hash。提交`feat(ui-editor): 建立CSharp界面制作与校验流程`。本Task不要求实现登录业务。

### Task U5：无业务登录示例与首帧就绪

**Files:**
- Create: `Assets/Scripts/Game/Entry.cs`、`Login/UILogin.cs`、`Login/UILogin.State.cs`、`Login/LocalSimulationSession.cs`。
- Generate: `Assets/Scripts/Game/Login/Generated/UILogin.G.Bindings.cs`；Create: `Assets/Content/UI/Login/UILogin.prefab`、`UILogin.Layout.asset`、`UILogin.Text.asset`、`UILogin.Config.asset`。
- Create: `Assets/Settings/Environment/DevelopmentProfile.asset`、`LocalSimulationProfile.asset`、`Tests/PlayMode/UILoginTests.cs`。
- Modify: `Assets/Scripts/Framework/Bootstrap/ClientBootstrap.cs`，将F3 Proof入口替换成最终热更入口，证明场景仍只放Tests。

**Interfaces:** `Entry:IHotfixEntry`创建普通C# `UILogin`对象；`UILogin.Bind(IViewHandle view,ISessionClient session)`、`Unbind()`定义在手写partial。Bind调用生成partial的`BindComponents(UIBindings bindings)`取得并校验Title、AccountInput、LoginButton、CancelButton、StatusLabel、VersionLabel，再注册事件；Unbind先取消请求/订阅，再`ClearBindings()`。两个文件属于同一Client.Game.Login.UILogin类型，不挂Prefab。ViewID=UILogin，Profile选择LocalSimulation或TokDevelopment。

- [x] **Step 1:** 写 `OneClickCreatesOneLogin`、`ButtonStatesFollowSessionState`、`RotateDuringLoginRetainsTextFocusAndRequest`、`CloseThenLateSuccessDoesNotTouchView`、`SimulationNeverClaimsServerAuthentication`；运行U5 PlayMode确认红灯。
- [x] **Step 2:** 使用U4模板/配置/绑定生成器制作登录页，遵循UI编辑规范；包括标题、测试账号、登录/取消、有限错误/状态、演示版本。模拟模式始终显示“本地模拟”；真实开发登录使用N3 demo凭据，不加密码注册、SDK或区服业务。
- [x] **Step 3:** 登录中禁重入、允许取消；成功停在本页显示“已登录（框架演示）”；拒绝/断线恢复按钮并如实显示。旋转不新建UILogin页面对象或session，不重绑控件/事件。账号/令牌不进入日志。
- [x] **Step 4:** 本地资源、Envelope自检和登录首帧完成后调用context.ReportReady一次；此时不要求远程登录成功。运行 `./Tool/Client/Test-Client.ps1 -Task U5 -Mode PlayMode`，20次开关与返回登录后事件/资源句柄回基线。
- [x] **Step 5:** 提交 `feat(login): 添加可热更自适配登录示例`。

### Task D1：统一Player/补丁构建与不可变基线归档

**Files:**
- Create: `Editor/Build/ClientBuild.cs`、`BuildRequest.cs`、`BuildResult.cs`、`PlayerBuilder.cs`、`PatchBuilder.cs`、`ResourcePackageBuilder.cs`、`BuildPreflight.cs`。
- Create: `Tool/Client/Build-Client.ps1`、`Tool/Client/Serve-TestReleases.ps1`、`Tests/EditMode/BuildContractTests.cs`、`docs/build.md`。
- Reuse/Integrate: F4的ManifestWriter、F6的PlayerBaselineArchive/CompatibilityCheck；不另造第二套产物格式。

**Interfaces:**
- `ClientBuild.Run()` 为唯一C#命令行入口；Editor菜单调用同一个 `ClientBuild.Execute(BuildRequest request):BuildResult`。
- `Build-Client.ps1 -Kind Player|Patch -Platform Android|Windows64 -OutputRoot <path> [-PlayerArchive <path>]`；Patch必须显式选PlayerArchive。
- `BuildResult`含Status/OutputDirectory/ResultFile/PlayerBuildId/ReleaseId/Errors/EvidenceFiles，所有入口使用返回的实际子目录；输出为新建 `Build_<UTC时间>_<短随机ID>`，结果不从根目录猜测。

- [ ] **Step 1:** 写`AllEntriesReturnSameResolvedOutput`、`PatchRequiresArchivedBaseline`、`HotfixAssembliesExcludedFromAot`、`SecondBuildForSameProjectIsRejected`、`FailureRestoresSourceSettings`、`UIEditSceneExcludedFromPlayer`、`InvalidUIBindingsBlockBuild`，运行D1 EditMode确认红灯。
- [ ] **Step 2:** 实现项目级互斥、预检、目标平台固定编译、全新输出和source snapshot/finally恢复；已有Unity进程不强杀。必要生成物写忽略staging并成对处理meta，不改原Prefab/材质/资源GUID。
- [ ] **Step 3:** Player路径：N1协议→热DLL编译→HybridCLR生成与裁剪预检→完整资源包/包内基线→Player构建→最终post-strip比对→归档原生hash/元数据/锁/配置。F8是技术证明基线，本Task生成覆盖最终UI与协议接口的正式框架基线。
- [ ] **Step 4:** Patch路径：读取原Player归档→编译目标热DLL→AOT兼容检查→原生Package完整资源快照→文件hash→签名发布清单→生成指向该清单hash的签名渠道文件。不能运行GenerateAll后替换原元数据，不能修改主包；失败不生成可用渠道指针。只输出本地产物，外部发布另按实际授权执行。签名测试服务器只监听显式开发地址。
- [ ] **Step 5:** 运行 `./Tool/Client/Test-Client.ps1 -Task D1 -Mode EditMode`；通过Editor/CLI各构建一次小fixture对比BuildResult语义；Android/Windows各生成最终Player和v2补丁。提交 `feat(build): 统一主包与兼容补丁交付`。

### Task D2：逐项故障注入、UI矩阵和两平台运行验收

**Files:**
- Create: `Tool/Client/Verify-Framework.ps1`、`Tests/Fixtures/Releases/FaultMatrix.json`、`docs/verification/framework-acceptance.md`。
- Modify: `Test-HotUpdate.ps1`、`Test-UIMatrix.ps1`、`Test-ProtocolInterop.ps1` 增加统一证据聚合，不放宽原断言。

**Interfaces:** `Verify-Framework.ps1 -Platform Android|Windows64 [-DeviceId <id>] -PlayerArchive <path>` 输出 `framework-result.json` 和TaskID到证据的映射；任何必测项未执行为NotRun，不能自动当Pass。

- [ ] **Step 1:** 先写判定器：缺平台、缺设备、旧runId证据、Playerhash变化、日志缺热更分支、required case未运行时总结果必须失败；用故意不完整fixture观察红灯。
- [ ] **Step 2:** 每平台独立验证：干净安装离线基线→在线v2更新→完整重启执行新C#分支和新UI资源→再次无网启动；APK/EXE及native binary hash不变。验证Protobuf集合、未知字段和新增fixture Parser真实执行。
- [ ] **Step 3:** 每平台故障矩阵：坏签名、坏hash、错PlayerBuildId、错架构、缺DLL/Bundle、坏原生版本表、磁盘预算不足、下载取消/网络中断。完整性/身份校验失败的候选在资源初始化前拒绝，旧版本可继续启动；坏原生版本表还必须覆盖合法签名/hash但GF语义损坏的独立用例：该候选根至多尝试初始化一次，保留GF失败证据、无热DLL加载/无Ready/无known-good晋升，要求完整重启后从原known-good实际启动。旧版本文件保持完整；所有计数按候选与实际选中版本区分，不在同进程二次切根。
- [ ] **Step 4:** 在download完成前、slot改名前、状态替换前后、trial写后、热入口执行中强杀；完整重启仅选完整可用版本。ThrowOnStart必须下次启动回退；登录服务器拒绝/断线不能误判坏补丁。强杀是测试控制，不引入原生崩溃插件。
- [ ] **Step 5:** Windows跑全部U2分辨率和窗口拖拽；Android在雷电中运行ARM64 APK，测竖→横左→竖→横右、软键盘/旋转、后台恢复、安全区和短表单滚动；保留输入/焦点、一次请求、2稳定帧内布局完成、交互区域在安全区内。记录主ABI与转译环境，真机相关项标NotRun。
- [ ] **Step 6:** 两平台执行 `./Tool/Client/Verify-Framework.ps1 -Platform Windows64 -PlayerArchive <Windows归档>` 与 `./Tool/Client/Verify-Framework.ps1 -Platform Android -DeviceId <Android序列号> -PlayerArchive <Android归档>`；首期 Android 使用用户选择的雷电环境，预期各 `FRAMEWORK_ACCEPTANCE_PASS`。人工运行项逐条记录操作者/时间/环境/截图；真实ARM硬件和未测项不得勾选。
- [ ] **Step 7:** 独立复核产物与结果后提交 `test(client): 完成热更新登录框架验收矩阵`。

### Task D3：启动文档、迁移台账与后续iOS清单

**Files:**
- Create: `README.md`、`docs/runtime-architecture.md`、`docs/hotupdate-release.md`、`docs/migration-ledger.md`、`docs/ios-follow-up.md`。
- Modify: 本总计划和三个子计划，只按实际证据更新checkbox与状态，不删除失败记录。

**Interfaces:** README从干净checkout到本地登录页的命令可执行；hotupdate-release文档分别说明新Player、原基线补丁、发布回滚和坏版下进程回退。台账每个旧模块记录Reference/Reimplemented/NotMigrated及理由，不把参考误写成复制完成。

- [ ] **Step 1:** 用干净checkout或隔离工作区照README执行依赖恢复、协议生成、测试、构建与启动；未写的必要个人配置/工具前置导致失败，先补文档再重跑。
- [ ] **Step 2:** 核对主包和热更边界：原生SDK/引擎/稳定契约/信封改变需要新Player；UI/资源/允许的C#业务改动走补丁。说明不能原地卸载程序集、网络恢复后置和生产安全接入后置。
- [ ] **Step 3:** iOS清单明确后续需Mac、兼容Unity/HybridCLR、Xcode工具链、签名、ARM64 Player、路径/后台/键盘/安全区、同主包v1→v2、坏补丁回退与分发审核验证；当前状态全部NotRun，不承诺可上架。
- [ ] **Step 4:** 按目录规范核对Assets/Scripts、Assets/Tests、生成物与共享工具Include，拒绝旧自研Package根残留或扫描零文件；搜索Framework/Game中UnityEditor、Windows路径、Lua/XLua引用，按实际条件编译检查；整合D2证据链接和已知限制。满足M0–M4才更新首期完成声明。
- [ ] **Step 5:** 提交 `docs(client): 记录框架交付与后续平台边界`。

## 首期最终演示脚本

1. 打开/安装主包，默认竖屏，看到明确模式的登录页。
2. 输入测试账号，发起登录；旋转时输入与在途状态保持；取消/错误/断线可恢复。
3. 发布v2，仅下载DLL和资源，关闭并重新启动同一主包。
4. 登录页布局/版本标记更新，新增C#分支和Protobuf fixture执行，主包hash不变。
5. 发布坏版，观察拒绝或下次启动自动选择上一可用版本；离线仍能打开登录页。

以上仅证明框架闭环，不增加游戏玩法，也不代表生产账号、完整会话恢复、渠道或iOS已验收。
