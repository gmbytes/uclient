# 迁移台账

参考工程为 `E:/hygd/work/client`，保持只读；新客户端为 `E:/sgame/client`，共享协议及工具位于 `E:/sgame/comm`。本台账对应[总计划第3节](superpowers/plans/2026-10-28-csharp-hotupdate-framework.md)的全部16项材料，记录实际参考入口、实现归属和首期边界。

状态含义：

- **Reference**：采用旧工程的问题、生命周期或制作流程作为设计输入。
- **Reimplemented**：能力由新 C# 代码或重新安装的官方依赖基线承担，具体归属写在表中。
- **NotMigrated**：该旧实现、数据或业务没有迁入首期。

一项材料可以同时包含参考机制、新实现和未迁旧代码。`Reimplemented` 描述实现归属；实际平台与故障验收以[实施进度](implementation-progress.md)为准。

下表旧路径相对参考工程；新路径中的 `Framework/`、`Game/`、`Editor/` 分别指 `Assets/Scripts/Framework/`、`Assets/Scripts/Game/`、`Assets/Scripts/Editor/`。

## 逐项迁移范围

| 材料 | 已核对的旧参考入口 | 处理状态与新落点 | 理由及边界 |
| --- | --- | --- | --- |
| Procedure/Fsm/Event/Pool/UI 基础模块 | `Assets/GameMain/Scripts/Base/GameEntry.Builtin.cs`；`Assets/GameFramework/Scripts/Runtime/{Procedure,Fsm,Event,ObjectPool,UI}/` | 注册习惯 **Reference**；官方 GF/UGF 安装与新适配 **Reimplemented**。落点为 `Assets/ThirdParty/GameFramework`、`Framework/Contracts/IClientContext.cs`、资源/UI 服务及两个启动 Prefab。 | 依赖来自锁定的官方基线。旧 `GameEntry` 全局接口、`GameEntry.Custom.cs` 的 XLua/SDK/游戏配置组合 **NotMigrated**。启动时仅启用所需基础组件。F1/F3/U3。 |
| 启动、更新、LoadingManager | `Assets/GameMain/Scripts/Procedure/{ProcedureInit,ProcedureUpdate,ProcedureLaunch,LoadingManager}.cs`；`Assets/GameMain/Scripts/UI/LoadingUIForm.cs` | 阶段、进度和完成协同 **Reference**；`Framework/Bootstrap/{ClientBootstrap,UIUpdate}.cs` 与 `Framework/Release/ReleaseCoordinator.cs` **Reimplemented**。 | 形成单一 AOT 启动流程。旧 Lua 登录、场景、BGM、相机及业务耦合 **NotMigrated**。最终双平台代码/资源更新由 F8/D2 验证。 |
| Addressables 定制资源链 | `Assets/GameFramework/Scripts/Runtime/Resource/AAResourceComponent.cs`；`Assets/GameFramework/Libraries/GameFramework/Resource/AddressablesComponent.cs` | 旧 fork 与 Addressables 实现 **NotMigrated**；官方 GF Package、`Framework/Resource/{ResourceSlotBootstrap,ResourceService}.cs` 和完整 release 目录 **Reimplemented**。 | 使用 GF 原生版本表与 Bundle 索引；初始化前注入资源路径，一个进程只激活一个资源根。唯一通用 vendor 补丁见[补丁说明](vendor-patches/ugf-resource-paths.md)。F2/F5。 |
| UICanvasScaler | `Assets/GameMain/Scripts/Components/UICanvasScaler.cs` | 旧实现 **NotMigrated**；`Framework/UI/{ScreenLayoutService,AdaptiveCanvas,AdaptiveLayout,AdaptiveLayoutDefinition}.cs` **Reimplemented**。 | 使用动态屏幕快照、浮点宽高判定和横竖双布局。参考分辨率为竖屏1080×1920、横屏1920×1080，单一全局 Scaler 的 match 为0.5。U1/U2。 |
| UISafeArea | `Assets/GameMain/Scripts/Components/UISafeArea.cs` 的 Refresh/GetSafeArea/ApplySafeArea | 归一化机制 **Reference**；`Framework/UI/SafeAreaRoot.cs` 与统一屏幕快照 **Reimplemented**。 | 交互区按安全区布局，背景满屏。旧机型模拟表、Mirror/Conform 选项和方向/状态栏补丁 **NotMigrated**；无效安全区按新规则处理。U1/U2/U4。 |
| Lua UI MVC | `Assets/GameMain/LuaFile/ui/core/{BaseWindow,BaseController,BaseView}.lua`；`Assets/GameMain/LuaFile/ui/UIManager.lua`；`Assets/GameMain/LuaFile/ui/login/` | 生命周期与对称解绑 **Reference**；`Framework/UI/{UIService,UIViewBridge,UIBindings}.cs` 及 `Game/Login/UILogin` partial **Reimplemented**。 | 热更 Presenter 是普通 C# 对象。Lua/XLua、旧 MVC 三脚本、旧登录业务和素材 **NotMigrated**；同窗口旋转保留状态。U3/U5。 |
| UI 模板、UIConfig、GUILink、导出与检查 | `Assets/GameMain/Scripts/Editor/Util/{ED_Generator,ED_PrefabUtility}.cs`；`Assets/GameMain/Scripts/Editor/UI/UIConfigManagerEditor.cs`；`Assets/Editor/NGUILinkEditor.cs`；`Assets/GameMain/Scripts/WhyNot/Lua/LuaExportData.cs` | 编辑工作流 **Reference**；`Editor/UI/` 下 Creator、Config、Bindings、Generator、Validator、Preview 工具 **Reimplemented**。 | 生成强类型 C# partial，显式保存控件引用，校验配置/命名/Prefab 结构。旧 Lua 路径、WN_LuaButton、每窗口 Scaler、固定 Expand、隐藏位置/零缩放规则和旧整包检查器 **NotMigrated**。完整来源与新规则见[UI制作规范](ui-authoring.md)。U4。 |
| MessagePack/JSON/PacketHandler | `Assets/GameMain/Scripts/Network/{NetworkChannelHelper,PacketBase,PacketHeader,PacketHandler}.cs` | 旧包格式及分发链 **NotMigrated**；固定 Protobuf Frame、`Framework/Network/Core/{FrameCodec,TcpFrameTransport,TcpFrameConnection,SessionClient}.cs` 与 Unity 主线程队列 **Reimplemented**。 | 新协议按 Tok Frame v3 接入，具有长度/期限/队列和连接代次边界；不承诺兼容旧 SLG 服务端。N0–N3。 |
| 重连器 | `Assets/GameMain/Scripts/Network/NetworkReconnectManager.cs` | 超时、断线、后台恢复等异常场景 **Reference**；旧重连器及 SDK 分支 **NotMigrated**。 | 首期明确终止断线/取消会话并拒绝迟到回调。完整 ACK、Resume、刷新恢复、重连回放留后续；不能将当前 `SessionClient` 写成完整旧重连迁移。N3。 |
| 配置启动会话与计时 | `Assets/GameMain/Scripts/ConfigLoading/{ConfigLoadSession,StartupTimeline}.cs`；`Assets/GameMain/Scripts/Components/GameConfigComponent.cs`；`Assets/GameMain/Scripts/Utillity/StartupTiming.cs` | 取消代次、完成屏障、阶段诊断 **Reference**；Bootstrap/Release 生命周期、完整目录提交屏障和 `ReleaseDiagnostics.cs` **Reimplemented**。 | 旧配置会话、游戏表、Lua 配置消费者 **NotMigrated**。诊断保留实际阶段和错误，版本就绪不依赖服务器认证成功。F3/F5/F7。 |
| Build/PowerShell 工具 | `Tool/ClientTools/{Build-Player,Resolve-BuildOutput}.ps1`；`Assets/GameMain/Scripts/WhyNot/Editor/Build/{Build.Shared,Build}.cs` | 单入口、明确输出、结果文件与配置恢复 **Reference**。新 Task 工具位于 `Tool/Client/`、`Editor/Build/`；统一最终构建由 D1 交付。 | 旧渠道和 Addressables 构建链 **NotMigrated**。已有 Smoke/Proof 工具证明各自 Task；D1 的统一 Player/Patch 入口、源设置恢复与最终归档仍需其独立完成证据。 |
| SDK/IAP/上报、技能/地图编辑器、SLG 业务与美术 | `Assets/GameMain/Scripts/WhyNot/SDK/SDKManager.cs`；`Assets/GameMain/Scripts/Components/{InAppPurchaseComponent,GameReportComponent}.cs`；`Packages/com.whynot.{skilleditor,skillruntime}`；`Assets/WorldMapEditor/`；`Assets/GameMain/Scripts/Logic/MainPlayer.cs`；`Assets/GameMain/Scripts/Procedure/{ProcedureBattle,ProcedureGame}.cs`；`Assets/Art/`、`Assets/GameMain/MapData/`、`Assets/GameMain/UI/` | 全部 **NotMigrated**。 | 首期只交付框架与无业务登录示例。地图、战斗、英雄、公会、经济、支付、生产认证、SDK 接入与旧美术以后按具体需求处理。 |
| `comm/proto` | Tok canonical `tok/crates/tok-proto/proto/tok/client/v1/client.proto` 与实际 codec/错误码来源 | canonical **Reference**；在 `comm/proto` 建立唯一共享源、来源锁与空正式业务准入，客户端保存生成 C#。 | 固定 Frame.version=3；改变固定信封需要新 Player 并协调服务端。所有可编辑测试 proto 也放 `comm/proto/tests/fixtures`。N0/N1。 |
| comm 通用生成/兼容工具 | `comm/tools/ProtocolGen/`、`Generate-Protocol.ps1`、`Validate-Boundaries.ps1`、`Test-Managed.ps1`、TokInterop 工具 | 既有共享工具 **Reference/复用**；CSharp 目标、明确输入根、来源锁和边界检查 **Reimplemented**。 | 缺必需源或零文件扫描应失败。CSharp Managed 编译根显式选择新客户端纯 C# 部分，保留其他消费者的 Legacy 行为。N1/N4。 |
| comm 业务 Lua 生成目标 | 共享 ProtocolGen 的既有 Lua 目标及消费者 | 既有工具目标保留；新增 CSharp 静态 Registry **Reimplemented**。新客户端 Lua/XLua 运行时 **NotMigrated**。 | 通过隔离 fixture 验证旧目标，避免为新客户端引入 Lua。正式 `Client.Game.Protocol` 注册表为空；测试协议和生成物分别隔离。N1。 |
| Tok | 当前 Tok canonical、codec、错误码与既有 demo TCP7000 | 协议和 demo 互操作 **Reference**；Tok 核心及旧 Java/SLG 兼容层 **NotMigrated**。 | 客户端使用实际 Frame v3 联调，不修改 Tok 核心。现有 Managed/Rust 互操作记录有明确运行范围；最终原生登录与版本健康关联由 D2 验证。详见[Tok接入契约](tok-demo-protocol.md)与[协议验证记录](verification/protocol.md)。 |

## 新 UI 制作边界

采用的制作流程为：模板 → 编辑 Prefab → 配置 → 显式绑定 → 生成 → 校验 → 预览。新规范统一使用 `UI` 大写，文件、ViewID 和 Prefab 根使用同一个 `UI{Name}`。

登录示例的 `UILogin.cs`、`UILogin.State.cs` 与 `Generated/UILogin.G.Bindings.cs` 同属 `Client.Game.Login.UILogin` 普通 `sealed partial`。生成器只处理控件绑定，手写状态与事件逻辑由开发者维护。可更新资源放 `Assets/Content`，自研代码放 `Assets/Scripts/Framework`、`Game`、`Editor`。

主窗口根包含 RectTransform、UIViewBridge、UIBindings；Canvas/CanvasGroup/GraphicRaycaster 位于 UINode。窗口挂在 `AdaptiveUIRoot/Windows/<GF组>/UI{Name}`，与全局 SafeArea 同级；窗口内局部安全区只应用一次，背景和模态遮罩覆盖全帧。详细编辑规则以[UI制作规范](ui-authoring.md)为准。

## 新 Release 层的归属

`Framework/Release` 按本计划实现签名清单、完整目录物化、原子状态、试启动、错误保留和下进程恢复。旧更新与 Loading 流程提供阶段展示和完成协同参考；这些发布事务由新框架承担。

候选 C 未完成试启动时，下次启动隔离 C，继续选择已验证的 K，并保留更早的 P。K 自身失败时再按原子状态转向可用的 P。一个进程只初始化一个资源根、加载一个热更程序集集合，失败后要求完整重启。

AOT 元数据按平台与原始 PlayerBuildId 归档；补丁使用原归档并通过兼容检查。原生 SDK、引擎、稳定契约、固定信封或 AOT 组成改变需要新 Player。最终发布构建、两平台原生热更/回退、完整故障/UI矩阵及干净工程复现分别由 D1、F8/D2、D3 提供证据。

## 首期验收与后续

本地模拟登录会永久标明模拟模式；Tok 开发入口使用开发协议与会话服务。生产认证、完整恢复协议和业务系统仍属后续范围。

首期验收为 Windows x64 与用户选择的雷电 Android ARM64 APK。模拟器记录主 ABI、ARM64 进程和转译环境；真实 ARM 硬件/性能与 iOS 不冒充通过。iOS 后续项见[iOS清单](ios-follow-up.md)。

完成声明须同时满足总计划 M0–M4，并对应当次原始证据。旧文件存在、机制被参考、代码已落地或历史 Editor 通过均有各自范围，不能单独代替最终原生交付验收。
