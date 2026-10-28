# 全 C# 竖屏客户端框架设计

日期：2026-10-28。2026-10-29用户已要求实施，当前状态见[实施进度](../../implementation-progress.md)；下面保留原设计基线，执行中的必要兼容调整记录于进度台账。

## 1. 已确认要求与范围

- 新客户端目录：`E:/sgame/client`；Unity工程已建立，已实施范围及验收证据见[实施进度](../../implementation-progress.md)。
- 自研代码使用传统`Assets/Scripts/Framework`、`Assets/Scripts/Game`、`Assets/Scripts/Editor`结构；分别承载AOT基础设施、C#热更应用和编辑器工具。完整目录规范见[工程目录规划](../../project-layout.md)。
- 参考项目：`E:/hygd/work/client`；参考其组织和工具经验，不复制完整游戏。
- 项目自有运行时代码全部使用 C#，不引入 Lua、XLua 或脚本语言业务层。
- 先制作框架和一个登录示例，不实现地图、战斗、英雄、联盟、付费等具体业务。
- 竖屏优先；UI 框架自动适应横屏、竖屏、窗口尺寸和安全区变化。
- 可使用 GameFramework；通信使用 Protobuf。
- 最高优先级是代码与资源热更新。
- 首期验收 Android、Windows；用户随后明确 iOS 不做首期验收，保留后续兼容和验收任务。
- 2026-10-29用户提出并启动雷电模拟器作为当前Android验证环境；首期用ARM64 APK在雷电中做功能验收，真实ARM64硬件兼容/性能后续单列且保持未验证。当前雷电14为Android14/API34，x86_64主ABI，arm64-v8a通过libhoudini转译。

以下技术选择由本计划提出，不冒充用户已逐项批准：Unity 2022.3.25f1、干净 GameFramework、HybridCLR 9.0.0 社区版、UGF 原生 Resource、uGUI + TextMeshPro、Google.Protobuf。Unity 版本取现有安装及参考项目基线；先做兼容性验证，再锁定依赖。如果此组合验证失败，记录最小失败和候选版本，先修订计划，不能静默换引擎大版本。

## 2. 方案取舍

| 方案 | 收益与代价 | 本提案选择 |
| --- | --- | --- |
| 干净 GameFramework + HybridCLR + 原生 Resource | C# 程序集热更，流程/资源/UI/下载复用框架；需要明确 AOT 边界和版本事务 | 推荐 |
| GameFramework + HybridCLR + Addressables | 可用，但增加资源适配与第二套版本关系；当前空工程没有必须保留的 Addressables 资产 | 首期不采用 |
| 复制旧客户端后删 Lua、SDK 和业务 | 短期目录齐全，实际会继承框架内 Lua UI、旧渠道和定制资源耦合 | 排除 |
| ILRuntime 等解释器方案 | 仍可全 C#，但需另一套绑定、泛型与兼容验证；当前需求没有必须选它的约束 | 备选，首期不并行集成 |

UGF 原生 Resource 是唯一的资产地址、依赖与 Bundle 索引。发布清单只负责文件完整性、程序集及版本兼容，不另建资产解析器。GameFramework 核心不写入项目私有逻辑；项目扩展放独立程序集。由于 UGF ResourceComponent 默认在 Start 固定路径，允许一个独立记录的通用补丁：在 Start 前注入资源路径，初始化后拒绝修改；该补丁不依赖项目类型。首期复用 Procedure/Fsm、Event、Resource、UI、Download、WebRequest、ReferencePool/ObjectPool、Setting、Debugger；Entity、Sound、Localization 仅保留接入位置，不提前制作业务包装。

## 3. 现有材料和迁移边界

旧项目的 `ResourceManager` 直接构造定制 Addressables；`UIForm`、`UIComponent` 注入 Lua UI；`BaseComponent` 含旧 SDK/Lua 模式。旧网络实际采用 MessagePack，再转 JSON 分发；不能据 `using ProtoBuf` 判断可复用。

旧 `UICanvasScaler` 只在 Awake 调整，固定 1920×1080，且宽高比使用整数除法，不能搬来满足新 UI 要求。`UISafeArea` 的归一化锚点算法可参考，旧机型补丁和默认关闭 X 轴适配不直接保留。详细迁移矩阵见总计划。

协议统一由`E:/sgame/comm/proto`提供共享构建源和来源锁；N0已按Tok canonical定义建立必需文件，测试源隔离在共享fixtures目录。Tok v3固定信封来自`tok/crates/tok-proto/proto/tok/client/v1/client.proto`的完整兼容副本，客户端不能单方面改线格式。生成工具显式接收共享ProtoRoot和protocol.lock；客户端只保留C#生成物。首期使用LoginRequest/LoginResponse/ControlError，正式业务准入保持为空；详细定义见[Tok demo接入契约](../../tok-demo-protocol.md)。

## 4. 程序集与目录边界

| 程序集 | 位置 | 编译方式与责任 |
| --- | --- | --- |
| `Client.Contracts` | `Assets/Scripts/Framework/Contracts` | AOT；跨层接口、错误和稳定 DTO，不引用热更程序集 |
| `Client.Framework` | `Assets/Scripts/Framework/Client.Framework.asmdef` | AOT；UGF 适配、资源、更新、屏幕/UI 宿主、网络和平台接口；嵌套独立asmdef子树归各自程序集 |
| `Client.Protocol.Envelope` | `Assets/Scripts/Framework/Protocol/Envelope` | AOT；固定 Frame v3 生成物，变更需要新 Player |
| `Client.Bootstrap` | `Assets/Scripts/Framework/Bootstrap` | AOT；唯一启动场景、内置更新界面和热更加载器 |
| `Client.Game.Protocol` | `Assets/Scripts/Game/Protocol` | 热更；未来业务生成类型与静态注册表；首期空注册表合法 |
| `Client.Game` | `Assets/Scripts/Game` | 热更；应用流程、登录 Presenter、后续业务入口 |
| `Client.Editor` / Tests | `Assets/Scripts/Editor`、`Assets/Tests/EditMode`、`Assets/Tests/PlayMode` | 仅 Editor/测试；不进入发行 Player |

依赖方向：Bootstrap→Framework/Contracts；Framework→Contracts/Envelope；Game→Contracts/Framework/Game.Protocol；Framework、Contracts、Envelope均不能引用Game。嵌套的Bootstrap、Contracts、Envelope和Game.Protocol各自有独立asmdef，不并入父程序集。UGF、HybridCLR.Runtime、Google.Protobuf和Unity UI运行库在AOT中；Google.Protobuf采用共享锁定3.36.2，先验证Unity/IL2CPP。

Game和Game.Protocol的asmdef设为非自动引用，并由HybridCLR配置从Player AOT编译中排除。Prefab只序列化Framework中通用AOT UIViewBridge/UIBindings等稳定组件；Game/Login/UILogin.cs与Generated/UILogin.G.Bindings.cs共同声明Client.Game.Login.UILogin普通C# partial类型，分别承载页面行为和生成控件绑定。程序集加载后创建和Bind，不把热更类型写入Prefab或AOT配置引用。

## 5. 热更新语义与启动流程

热更新指：主包安装后，在同一个 Player 基线上下载新的 C# DLL 和 Unity 资源，启动时加载执行。运行期间可检查/下载下一版，但已加载程序集不原地替换；下一次完整启动生效。普通返回登录复用当前热更代次，不重复 `Assembly.Load`。

```text
Bootstrap（内置 AOT 更新 UI）
 → 读取本机上次启动状态/选择已知可用版本
 → 检查签名渠道清单与 Player 兼容性
 → 下载候选版本到 staging，校验完整文件集合
 → 原子提交候选状态，确定本进程唯一 release
 → 初始化该 release 的 UGF 资源路径
 → 加载当前 Player 专属 AOT 补充元数据
 → 按依赖顺序加载 Client.Game.Protocol、Client.Game
 → 调用 IHotfixEntry.InitializeAsync
 → 打开登录示例，首帧完成后 ReportReady
```

发布物必须包含：`releaseId`、`playerBuildId`、平台/架构、Unity 版本、稳定契约版本、固定信封 SHA、协议产物 SHA、AOT 元数据集合标识、程序集依赖与加载顺序、原生资源版本列表及文件清单。每个文件记录规范相对路径、长度、SHA-256。清单原始 UTF-8 字节使用 RSA-2048/SHA-256 分离签名；运行时只持有公钥，签名私钥不进入仓库或客户端。

正式配置下载仅 HTTPS；开发配置可显式使用本机/局域网 HTTP。签名不替代 HTTPS。清单最大 256 KiB，拒绝重复 JSON 字段、绝对路径、`..`、重复输出路径、错误平台和兼容标识。文件 hash 校验后才可加载 DLL。

UGF 下载模块用于文件传输；原生 Resource 使用 **Package 模式 + 每个 slot 完整资源快照**，原生包版本列表和 Bundle 产物保持格式。本期不启用 Updatable 混合目录或 GF 虚拟文件系统合包。版本目录形如 `persistentDataPath/Releases/<playerBuildId>/<releaseId>`；其 Resources 子目录作为 ResourceManager.ReadOnlyPath。下载只传变化文件，未变文件从已校验旧 slot 复制，最后新 slot 仍包含完整文件，不依赖可变共享目录。代价是至少两份资源磁盘空间，首期接受，后续再评估不可变文件去重。

包内基线先通过 UnityWebRequest 等平台安全读取方式物化到 slot，不能在 Android 对 jar 内 StreamingAssets 直接 File.Copy。启动场景只启用基础模块和 AOT UI；GameRoot.prefab 中的 Resource/UI 延迟创建。确定并注入路径后才激活 ResourceComponent，保证在其 SetResourceMode 和加载代理创建之前完成，整个进程不换根。这个初始化顺序必须先做原型验证。

下载中断/校验失败：旧版本保持可用。候选运行前写启动记录；达到登录页首帧就绪后才标记为 known-good。候选在 Resource 初始化以后失败，保留错误、标记坏版本并要求完整重启；下一进程回退上一已知可用版本。不能在同进程“卸载旧 DLL 再加载另一版”。首次无网络用包内基线；当前与上一可用版本至少保留两套，只清理不被状态文件引用的目录。

AOT 补充元数据必须来自该平台、该 Player 构建归档的 post-strip DLL；发布补丁时复用归档，不能重新生成一套替换。AOT API、原生库/SDK、引擎、稳定契约、固定信封、被裁掉且未预留的 AOT 类型变更，进入新主包流程。新增热更程序集/泛型用法也必须通过兼容门禁和真实 IL2CPP 验证，不承诺任意修改都能热更。

## 6. 框架服务与生命周期

稳定契约采用显式 `IClientContext`，提供 `IResourceService`、`IUIService`、`ISessionClient`、`ILogService`、生命周期 CancellationToken 和 `ReportReady`。不复制旧项目几十个全局单例的 Init/Destroy 手工列表。

`IHotfixEntry.InitializeAsync(IClientContext, CancellationToken)` 只执行一次；`ShutdownAsync(CancellationToken)` 取消页面请求、移除监听和释放 UI/资源句柄。异步结果携带连接/页面/启动代次，过期结果不得刷新新界面。Unity API 和 UI 回调固定回主线程。框架配置只含运行环境、更新地址、平台参数和 UI 定义，不迁移全部游戏表或旧 CouchDB。

## 7. 自动横竖屏 UI

采用 uGUI + TextMeshPro，默认启动竖屏；允许横向两个方向，关闭倒置竖屏。Windows 默认客户区 720×1280，可自由缩放窗口。

- `ScreenLayoutService` 监听实际宽高、安全区和方向变化；以有效客户区宽高判定布局，不能只信方向枚举。
- 宽高比 `width / (float)height >= 1` 为横屏，否则竖屏；零尺寸保持最近有效快照。相同快照不重复发布，同帧多次变化合并一次。
- 竖屏参考 1080×1920，横屏参考 1920×1080；CanvasScaler 使用 Scale With Screen Size，matchWidthOrHeight=0.5。
- 背景满屏；交互内容放 SafeAreaRoot，安全区裁到客户区后归一化。零/无效安全区回退完整客户区并记录一次诊断。
- 登录窗口是同一个实例、同一个 Presenter；Portrait 布局为纵向品牌区和表单，Landscape 为左右/紧凑布局。转换布局只变布局容器属性和锚点，不重建输入控件，不清空文字、焦点或在途状态。
- Login 最小表单区：左右边距 48 参考像素，表单宽度不超过 760，按钮高度至少 96 参考像素；极短窗口允许 ScrollRect，确保输入与主按钮可到达。
- 移动键盘：获得真实可用键盘区域时让当前输入及按钮可滚到可见位置；API不提供有效区域时使用可滚动表单，不硬编码机型高度。当前Android在雷电中验收旋转与软键盘，真机与iOS后续单列。
- 常规旋转在稳定尺寸到达后 2 个渲染帧内完成；验收时点击目标不能移出安全区，窗口层级/遮罩/返回键生命周期不变。

UI 分组固定为 Background、Page、Popup、Loading、System；采用 UGF UIForm 生命周期与 AOT Bridge，热更 Presenter 绑定/解绑监听。后续页面可提供双布局参数，不能宣称任意历史横屏 Prefab 无需制作即可自动变成竖屏。

UI编辑采用参考工程实际的模板→Prefab→配置→显式引用→代码生成→合规检查流程，具体见[UI编辑规范](../../ui-authoring.md)。主窗口宿主与子Canvas分离，根UIViewBridge委托UIBindings；嵌套控件只使用UIBindings。首期统一Overlay画布和根CanvasScaler，旧1920×1080/Expand及Lua MVC不直接沿用。U4提供创建/配置/绑定生成/校验/独立UIEdit预览场景，U5按此流程制作登录页。

## 8. Protobuf、连接与登录示例

共享协议源放`E:/sgame/comm/proto`。其中tok/client/v1与服务端canonical锁定，固定信封SHA和Frame.version=3不变；后续game/业务协议在同目录由两端协同维护。业务C#类与Parser/MessageId注册表未来放热更程序集；空registry必须能启动。首期以测试fixtures验证新增业务类型可热更，不向正式game/填占位业务。

网络采用项目自有的有界 C# TCP transport，由 GameFramework 组件宿主管理生命周期并在主线程发布事件；不再强行套用原生 NetworkChannel 的内部队列，以便精确控制现有 Tok 协议的分帧期限和背压。保留 `IFrameTransport` 供后续加密传输适配，不复制旧 MessagePack/JSON/XLua 链。包长 4 字节大端，正文最大 1,048,576 字节，先校验再分配；发送队列和主线程接收队列各最多 64 帧，均另设总字节 4 MiB 上限。完整帧 I/O 期限 15 秒，连接/登录期限各 5 秒，取消关闭当前连接代次。禁止无界缓存和未知结果业务请求的自动重发。

首期 `ISessionClient` 覆盖连接、LoginRequest、LoginResponse、有限错误、取消、断线和重复登录互斥；不实现完整 ACK/Resume/刷新状态机，但接口/状态不伪装已经支持它。收到非预期业务下行时关闭并返回 ProtocolError，安全诊断码为 UnsupportedMessage，不发送虚假 ACK。后续恢复扩展必须遵守 Tok 的连续序列、最大请求 ID 和 RecoveryComplete 约束。

当前 Tok 开发网关为明文 TCP、没有独立心跳，登录后空闲也受 I/O 期限限制；首期不发自造 ping/pong。生产认证和加密接入是后续服务端协同任务，不把开发凭据当上线方案。

登录 UI 只有标题、测试账号、登录/取消、状态/错误、客户端演示版本；无注册、角色、区服、公会或主城。默认开发服务明确标识“本地模拟”，不联网成功不展示“服务器已认证”。真实开发模式把正整数账号经 `DevelopmentCredentialProvider` 转为 `demo:<id>`，使用 Tok DemoAuthenticator；不虚构 HTTP 密码登录 API，不记录 credential/token。

成功后仍停在登录示例，显示“已登录（框架演示）”；断线如实恢复按钮和显示状态。一个C#回环测试服务提供成功/失败/延迟/半帧场景。真实Tok demo使用local.toml和TCP127.0.0.1:7000，凭据demo:42；7001是内部gRPC。AndroidUSB联调用adb reverse连接主机回环服务，具体步骤见接入契约；不修改Tok核心。

## 9. 验收边界

首期必须在Windows x64 IL2CPP Player和用户选择的雷电Android环境中运行ARM64 APK，完成包内启动、离线基线、v1→v2 DLL+UI资源热更、同APK/EXE hash不变、新C#分支、Protobuf类型/泛型、坏补丁拒绝及重启回退、横竖屏/软键盘、登录成功/错误/取消/断线。Editor或Mono不代替。记录模拟器主ABI、转译器和APK/process ABI，不将模拟器结果宣称为真实ARM硬件/性能验收。

iOS只保留无Windows专属依赖的Framework、平台路径抽象和后续Mac/Xcode/真机清单；未实测一律标为未验证。商店发布、生产CDN、真实账号认证、证书和隐私接入、业务玩法不在本期完成声明内。

## 10. 官方依据（查阅于 2026-10-28）

- HybridCLR 9 支持范围与安装：[安装](https://www.hybridclr.cn/docs/basic/install)。Windows Player 验证需安装 IL2CPP 模块。
- 社区版采用补充元数据方案，归档和目标平台不能混用：[AOT 泛型](https://www.hybridclr.cn/docs/basic/aotgeneric)。本提案不依赖商业版 full generic sharing。
- [GameFramework 官方源码与模块说明](https://github.com/EllanJiang/UnityGameFramework)，扩展通过项目适配层实现。
- [Unity CanvasScaler](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-CanvasScaler.html)、[Screen.safeArea](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Screen-safeArea.html)。
- [Protobuf C# 官方教程](https://protobuf.dev/getting-started/csharptutorial/)。项目版本以共享锁文件为准，不使用教程示例版本替换锁。

本地依据：`tok/crates/tok-proto/proto/tok/client/v1/client.proto`、`tok/examples/demo-game/config/local.toml`、`tok/examples/demo-game/src/gateway.rs`、`comm/tools/ProtocolGen/README.md`、`comm/tools/ProtocolGen/schema.lock.json`、`tok/docs/client-session.md`。comm/proto旧内容已删除，不作为现行依据；旧工程只读分析结果用于迁移取舍，不把其历史构建记录当新工程验证结果。
