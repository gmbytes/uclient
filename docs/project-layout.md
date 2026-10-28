# 客户端工程目录规划

日期：2026-10-28。状态：实施前目录规范。目标根目录为 `E:/sgame/client`；本轮只修改文档，下面的 Unity 工程目录尚未创建。

## 1. 目录原则

- 自研客户端脚本统一放在 `Assets/Scripts`，使用传统 Unity 工程结构。
- 一级目录收敛为Framework、Game、Editor：Framework放AOT基础设施和启动器，Game放C#热更应用与业务，Editor放编辑器工具。
- 程序集由各目录的 `.asmdef` 明确划分；AOT/热更新边界由依赖和构建配置控制。
- 自研类型、接口、属性、方法、文件和资源名称中的UI缩写统一大写，例如IUIService、UIService、UIViewBridge、UI属性和Test-UIMatrix.ps1；第三方原有名称保留。
- Prefab按用途命名。启动主干统一为GameFramework（基础框架组件）、GameRoot（资源/UI组件根节点）、UIUpdate（更新界面）；更新界面脚本统一为UIUpdate.cs。
- 窗口统一UI{Name}标识：UILogin.cs、UILogin.G.Bindings.cs、UILogin.prefab及UILogin.Config/Layout/Text.asset；手写和生成文件是同一partial类，细则见UI编辑规范。
- 场景、Prefab、配置资产、可更新内容与脚本分别管理。
- 第三方代码保留上游结构、版本和许可；自研代码不做本地 UPM 包。
- Unity测试放`Assets/Tests`；包含多版同名C#类型的fixture和独立.NET测试工程放根目录`Tests`，防止被Unity自动编译。所有可编辑测试.proto也统一放comm/proto的隔离测试目录，客户端只留生成物和二进制向量。
- `Tool`只放开发和构建入口；`Builds`只放可再生成产物、日志和验收证据。

## 2. 完整目标结构

```text
client/
├─ Assets/
│  ├─ Scenes/
│  │  └─ Bootstrap.unity                    唯一随Player直接启动的场景
│  ├─ Scripts/
│  │  ├─ Framework/                         AOT基础设施
│  │  │  ├─ Client.Framework.asmdef
│  │  │  ├─ Bootstrap/                      独立AOT启动程序集
│  │  │  │  ├─ Client.Bootstrap.asmdef
│  │  │  │  ├─ ClientBootstrap.cs
│  │  │  │  ├─ UIUpdate.cs
│  │  │  │  └─ link.xml
│  │  │  ├─ Contracts/                      稳定跨程序集接口和DTO
│  │  │  │  ├─ Client.Contracts.asmdef
│  │  │  │  ├─ IClientContext.cs
│  │  │  │  ├─ IHotfixEntry.cs
│  │  │  │  ├─ IResourceService.cs
│  │  │  │  ├─ IUIService.cs
│  │  │  │  ├─ ISessionClient.cs
│  │  │  │  └─ SessionContracts.cs
│  │  │  ├─ Core/                           纯C#基础类型、时钟和取消辅助
│  │  │  ├─ Release/                        签名清单、下载、slot、状态与回滚
│  │  │  ├─ Resource/                       原生GF资源适配、加载句柄与路径
│  │  │  ├─ HotUpdate/                      AOT元数据、DLL加载和兼容检查
│  │  │  ├─ Protocol/
│  │  │  │  ├─ IMessageRegistry.cs          业务Parser注册表稳定接口
│  │  │  │  └─ Envelope/                   固定Tok信封，属于AOT
│  │  │  │     ├─ Client.Protocol.Envelope.asmdef
│  │  │  │     ├─ Generated/Client.cs
│  │  │  │     └─ source-provenance.json
│  │  │  ├─ Network/
│  │  │  │  ├─ Core/                       纯C#分帧、transport、会话状态
│  │  │  │  └─ Unity/                      GF宿主与主线程事件适配
│  │  │  ├─ UI/                            屏幕快照、安全区、布局、窗口Bridge
│  │  │  └─ Platform/                      路径/应用生命周期等平台接口
│  │  ├─ Game/                              可热更应用与业务
│  │  │  ├─ Client.Game.asmdef
│  │  │  ├─ Entry.cs
│  │  │  ├─ Login/                         首期登录窗口
│  │  │  │  ├─ UILogin.cs                  手写partial，页面行为与事件
│  │  │  │  ├─ UILogin.State.cs             页面状态
│  │  │  │  └─ Generated/UILogin.G.Bindings.cs 同一partial，生成控件引用
│  │  │  └─ Protocol/                      可热更业务类型与静态注册表
│  │  │  │  ├─ Client.Game.Protocol.asmdef
│  │  │  │  └─ Generated/GameMessageRegistry.cs
│  │  └─ Editor/                           自研Unity编辑器代码
│  │     ├─ Client.Editor.asmdef
│  │     ├─ Build/                         Player/补丁/资源包构建与门禁
│  │     └─ UI/                            模板、配置、绑定生成、校验与预览
│  ├─ Prefabs/
│  │  └─ App/
│  │     ├─ GameFramework.prefab            基础框架组件
│  │     ├─ GameRoot.prefab                 资源与UI组件根节点
│  │     └─ UIUpdate.prefab                 更新进度、错误和重试界面
│  ├─ Content/                         通过GF资源包更新的内容
│  │  └─ UI/
│  │     ├─ Common/                        公共UI根、窗口定义和基础控件
│  │     └─ Login/                         登录Prefab、布局和文案资产
│  ├─ Settings/
│  │  ├─ Environment/                      模拟/开发环境配置资产
│  │  ├─ Build/                            构建模板和资源收集配置
│  │  └─ UI/                               全局UI层级等工程配置
│  ├─ ThirdParty/
│  │  ├─ GameFramework/                    官方UGF源码快照、GF库、许可
│  │  └─ Protobuf/                         共享锁定的Google.Protobuf依赖
│  ├─ Tests/
│  │  ├─ EditMode/
│  │  │  └─ Client.EditMode.Tests.asmdef
│  │  └─ PlayMode/
│  │     └─ Client.PlayMode.Tests.asmdef
│  └─ StreamingAssets/                     自动生成的包内基线，禁止手改
├─ Packages/
│  ├─ manifest.json                        Unity官方包/HybridCLR等UPM依赖
│  └─ packages-lock.json
├─ ProjectSettings/                        Unity工程设置，进入版本管理
├─ Tool/
│  └─ Client/                              测试、构建、热更与验收入口
├─ Tests/
│  ├─ Fixtures/                            多版本DLL/协议二进制向量/坏补丁
│  └─ Managed/                             独立.NET测试工程和纯C#测试源码
├─ Builds/                                 忽略的构建产物与测试证据
│  ├─ Player/<platform>/<runId>/            Player、基线归档、构建结果
│  ├─ Patch/<platform>/<releaseId>/         签名清单、DLL、元数据、资源包
│  ├─ Tests/<taskId>/<runId>/               每个Task的测试结果
│  └─ ProtocolInterop/<runId>/              C#/Tok黄金帧与互操作证据
├─ HybridCLRData/                          插件生成的本地构建数据
├─ docs/
│  ├─ project-layout.md                    本规范
│  ├─ ui-authoring.md                      UI编辑、制作和交付规范
│  ├─ dependencies.lock.json               依赖版本/提交/hash与来源
│  ├─ vendor-patches/                      通用上游补丁记录
│  ├─ verification/                        验收范围与证据说明
│  └─ superpowers/{specs,plans}/            设计、Task计划和进度
├─ .gitignore / .gitattributes / .editorconfig
└─ README.md
```

上表是完整目标树，不要求F1创建所有空目录。各目录由拥有实际文件的Task首次创建，便于逐项验证。插件额外生成的路径遵循锁定版本的官方配置，先记录再分类管理，不为统一外观强移插件文件。

## 3. 程序集位置和依赖

| 程序集 | asmdef位置 | 构建属性 |
| --- | --- | --- |
| Client.Contracts | Assets/Scripts/Framework/Contracts | AOT稳定接口；不引用Client.Framework或Game |
| Client.Protocol.Envelope | Assets/Scripts/Framework/Protocol/Envelope | AOT固定信封；引用Google.Protobuf |
| Client.Framework | Assets/Scripts/Framework | AOT基础设施；引用Contracts、Envelope、GF等 |
| Client.Bootstrap | Assets/Scripts/Framework/Bootstrap | AOT启动；引用Contracts/Framework，动态启动Game |
| Client.Game.Protocol | Assets/Scripts/Game/Protocol | 热更；引用Google.Protobuf及IMessageRegistry所属Framework |
| Client.Game | Assets/Scripts/Game | 热更；引用Contracts/Framework/Game.Protocol |
| Client.Editor | Assets/Scripts/Editor | includePlatforms=Editor；构建和资产工具 |
| Client.EditMode.Tests | Assets/Tests/EditMode | 仅Editor测试程序集 |
| Client.PlayMode.Tests | Assets/Tests/PlayMode | 仅测试构建启用；发行Player不包含 |

Framework根的asmdef不会吞并有独立asmdef的Bootstrap、Contracts和Envelope子树；Game根的asmdef也不包含独立的Game.Protocol子树。`Assets/Scripts`根不放统一大asmdef；自研源码都归属明确程序集。

Game目录名称本身不提供热更新隔离。Client.Game和Client.Game.Protocol保留autoReferenced=false，并进入HybridCLR热更程序集列表；发行Player中排除其AOT编译。当前尚未实施，程序集名称随本次目录规范确定；D1建立Player基线后名称、入口类型与发布契约保持稳定。

依赖方向固定为Bootstrap→Framework/Contracts，Framework→Contracts/Envelope，Game→Framework/Contracts/Game.Protocol；Framework、Contracts、Envelope均不得引用Game，Framework也不能反向引用Bootstrap。Bootstrap选择版本和调用稳定热更入口；Game.Entry初始化应用并打开登录页，二者不各自维护一套更新流程。

## 4. 放置新文件的规则

| 新增内容 | 位置 | 说明 |
| --- | --- | --- |
| 更新器、签名、资源/网络底层 | Scripts/Framework对应功能目录 | AOT；变更要判断是否需要新Player |
| 新页面流程/交互/未来业务 | Scripts/Game/<功能> | C#热更；首期Game/Login，按功能聚合 |
| 登录页绑定和页面状态 | Scripts/Game/Login | 纯C#绑定适配；Prefab只序列化通用AOT UIViewBridge |
| 自动生成页面绑定 | Scripts/Game/<功能>/Generated/UI{Name}.G.Bindings.cs | 同名partial类；仅写控件绑定，保护手写UI{Name}.cs |
| 新业务Protobuf生成类 | Scripts/Game/Protocol/Generated | 由comm工具生成，禁止手改 |
| 固定Frame生成代码 | Scripts/Framework/Protocol/Envelope/Generated | 单独AOT程序集，固定源hash |
| 页面Prefab、布局和文案 | Content/UI/<功能> | 资源包更新；仅序列化稳定AOT Bridge |
| 启动必需Prefab/组件 | Prefabs/App与Scripts/Framework/Bootstrap | 随主包；不引用热更类型和热更资源 |
| 项目配置资产 | Settings对应目录 | 示例配置进Git；凭据/个人覆盖不进Git |
| Unity测试 | Assets/Tests/EditMode或PlayMode | 带独立测试asmdef与Task Category |
| DLL V1/V2同名类型、坏包、协议二进制向量 | 根Tests/Fixtures | 不放Assets防止多版本源码被一起编译 |
| 可编辑协议测试源 | comm/proto/tests/fixtures | 与正式game源隔离，不参与正式协议收集 |
| 独立.NET测试项目 | 根Tests/Managed | 显式引用纯C#源，不通配整个Scripts |
| 构建/测试辅助脚本 | Tool/Client | 只编排，不承载客户端业务 |

代码目录使用`Resource`单数，避免和Unity特殊目录`Resources`混淆。自研可更新资源统一进入GF资源收集，不另建可编辑的第二套资源索引。第三方TMP等必需资源按原工具管理。

## 5. 与共享工程的边界

```text
E:/sgame/
  client/     Unity客户端与自己的测试/产物
  comm/
    proto/    共享协议唯一构建源、Tok兼容信封、来源锁与准入表
    tools/    生成器和跨语言验证工具
    xls/      表格
  tok/        服务端框架与现有demo，首期不改核心
  msg/        既有参考项目，首期不作为新客户端源码目录
```

- 共享协议源统一放`comm/proto`；tok固定信封从服务端canonical建立受锁控制的完整兼容副本，未来游戏协议由两端在同目录协同维护。客户端只保留两个Generated目录中的C#和provenance。
- `comm/tools/Managed/SGame.Client.Managed.csproj`改为显式Include `Assets/Scripts/Framework/Core`、`Protocol`、`Network/Core`及纯SessionContracts/ISessionClient等必要文件，继续排除Unity适配、UI、Bootstrap和Editor；禁止`Assets/Scripts/**/*.cs`大通配。
- `comm/tools/Validate-Boundaries.ps1`改为检查新根和程序集依赖；必需目录缺失或扫描零文件应失败，不能跳过检查后显示成功。
- Unity测试按程序集/测试全名或Category发现，移动文件不替代测试asmdef；runner仍拒绝零测试。
- 根`Tests/Fixtures`和`Tests/Managed`继续放C#测试、DLL和二进制向量；可编辑.proto例外统一转到comm/proto/tests/fixtures，所有读取者显式更新，不能保留隐藏客户端协议副本。
- 不复制tok内部RPC定义；7001内部gRPC不作为客户端协议。Tok demo登录使用TCP7000，详情见[接入契约](tok-demo-protocol.md)。

## 6. 版本管理与运行时路径

进入Git：自研源码、asmdef、人工维护的场景/Prefab/配置及meta、依赖锁、第三方许可、计划与Task测试源。comm/proto源、来源锁、正式生成物及provenance进入Git，生成命令须可重复且检查无漂移。

不进入Git：Library、Temp、Logs、obj/bin、Builds、个人环境覆盖、任何签名私钥、下载缓存及生成StreamingAssets基线。HybridCLR插件配置中必须保留的部分按官方版本逐项列入允许清单，构建缓存和大产物忽略，不能整个目录一刀切导致丢配置。

运行时下载不写Assets：使用`Application.persistentDataPath/Releases/<playerBuildId>/<releaseId>`。版本目录内`Resources/`是磁盘资源快照，不是Unity工程内的特殊Resources目录。不同平台和PlayerBuildId分开归档和运行，不能共用元数据。

## 7. 由哪些Task落地

- F1创建基础目录、第三方快照、测试asmdef和锁。
- F2/F3创建Resource适配、Bootstrap和程序集边界。
- F4–F8完成Release/HotUpdate及其测试。
- N0建立comm/proto与Tok兼容锁；N1/N4更新两个Generated落点、所有显式ProtoRoot、Managed Include与边界检查脚本。
- N2/N3创建Network/Core、Network/Unity及登录协议fixture。
- U1/U2创建UI基础设施；U3创建宿主和UIBindings；U4落实[UI编辑规范](ui-authoring.md)的工具；U5制作Content/UI与热更Login。
- D1–D3统一构建/证据目录，核对工程不包含旧的自研Package路径。

详细步骤与通过条件见[总计划](superpowers/plans/2026-10-28-csharp-hotupdate-framework.md)及其21个Task。
