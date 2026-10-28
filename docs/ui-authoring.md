# UI 编辑与制作规范

日期：2026-10-29。状态：新项目实施规范提案；依据参考工程实际工具和样例整理，本轮只编写文档，菜单、生成器和模板由Task U4实现。

## 1. 参考依据与采用范围

参考工程：`E:/hygd/work/client`。采用“创建模板 → 编辑Prefab → 注册配置 → 显式绑定 → 生成C# → 校验 → 横竖屏预览”的工作流。

| 参考工程已有能力 | 证据 | 新项目处理 |
| --- | --- | --- |
| UI模板生成根节点、UINode和PoolNode | [ED_Generator.cs:173](E:/hygd/work/client/Assets/GameMain/Scripts/Editor/Util/ED_Generator.cs:173) | 保留宿主/可视节点/池节点分离，生成全C#模板 |
| 主UI的Canvas不挂Prefab根；校验CanvasGroup及嵌套UI标记 | [ED_PrefabUtility.cs:119](E:/hygd/work/client/Assets/GameMain/Scripts/Editor/Util/ED_PrefabUtility.cs:119) | 转为UIViewBridge与主窗口/子组件规则 |
| 集中维护UIConfig | [UIConfigManagerEditor.cs:22](E:/hygd/work/client/Assets/GameMain/Scripts/Editor/UI/UIConfigManagerEditor.cs:22) | 维护UIViewDefinition和窗口层级，移除Lua MVC路径 |
| 字段名、类型、组件引用的显式导出列表 | [LuaExportData.cs:35](E:/hygd/work/client/Assets/GameMain/Scripts/WhyNot/Lua/LuaExportData.cs:35) | UIViewBridge引用表+Game层强类型C#绑定 |
| 添加控件到GUILink、重复名称检查和C#代码生成 | [NGUILinkEditor.cs:9](E:/hygd/work/client/Assets/Editor/NGUILinkEditor.cs:9)、[第91行](E:/hygd/work/client/Assets/Editor/NGUILinkEditor.cs:91) | 保留编辑体验，生成UI{Name}.G.Bindings.cs并保护手写逻辑 |
| 公共组件嵌套Prefab连接检查 | [ED_PrefabUtility.cs:184](E:/hygd/work/client/Assets/GameMain/Scripts/Editor/Util/ED_PrefabUtility.cs:184) | 项目公共组件保留Prefab连接，支持有依据的Variant |
| 锚点适配的Undo和Dirty处理 | [RectTransformAnchorAdapter.cs:8](E:/hygd/work/client/Assets/GameMain/Scripts/Editor/UI/RectTransformAnchorAdapter.cs:8) | 新工具支持Undo，修改后正确保存Prefab和配置 |

以下属于参考样例或旧框架实现，不作为新规范：固定1920×1080/Expand、每个窗口自行缩放、Lua MVC三脚本、WN_LuaButton、旧机型补丁、用隐藏位置或scale=0管理显示。旧登录页控件前缀并不统一，因此本规范不宣称旧项目有一套强制前缀规则。

## 2. 文件归属

```text
Assets/
  Scripts/
    Framework/UI/                          通用宿主、布局、安全区和绑定表
    Game/Login/
      UILogin.cs                          手写partial，页面行为与事件
      UILogin.State.cs                     可选页面状态
      Generated/UILogin.G.Bindings.cs      同一partial类型，自动生成控件绑定
    Editor/UI/                             模板、配置、绑定、校验和预览工具
  Content/UI/
    Common/                                公共控件和基础模板
    Login/
      UILogin.prefab
      UILogin.Layout.asset
      UILogin.Text.asset
      UILogin.Config.asset
  Scenes/UIEdit.unity                       独立编辑预览场景，不进入Player
```

Prefab和.meta成对维护；重命名/移动通过Unity资产操作保留GUID。Generated代码禁止手改，生成器仅覆盖.G.Bindings.cs，不覆盖UI{Name}.cs及State文件。正式绑定生成物进入Git，重复生成无差异。

## 3. 主窗口模板

```text
UILogin                                    RectTransform + UIViewBridge + UIBindings
├─ UINode                                  Canvas + CanvasGroup + GraphicRaycaster
│  ├─ Background                           满屏背景，不接收射线
│  └─ SafeArea                             SafeAreaRoot，交互安全区
│     ├─ Static                            标题、装饰等低频内容
│     └─ Dynamic                           输入、按钮、列表和状态
└─ PoolNode                                非显示的临时池/模板节点
```

- 根名称与Prefab名称一致，默认localScale=1、localRotation=0，根不挂Canvas。UIViewBridge引用窗口RootCanvas/CanvasGroup/UIBindings及LocalSafeArea；绑定表只引用该窗口内部组件。
- 全局挂载路径为AdaptiveUIRoot/Windows/<GF Group>/UI{Name}；Windows为全局SafeArea的全屏同级节点。窗口根、UINode、Background与模态Mask铺满完整画面；交互只在窗口自己的UINode/SafeArea下。UIViewBridge.Apply统一按LocalSafeArea→AdaptiveLayout→Keyboard基线顺序消费同一U1快照，运行时与UIEdit共用该流程；全局SafeArea仅服务全局内容，不能再包裹Windows。
- 主窗口Canvas放在UINode；根保持稳定宿主。主窗口配置有唯一ViewID，子控件不登记成独立窗口。
- 首期由GameRoot的UI根统一使用Screen Space Overlay和CanvasScaler；窗口内Canvas继承根画布环境，不再挂独立CanvasScaler。后续3D UI另设模板。
- Bootstrap的UIUpdate是主包内置启动界面：在GameRoot启用前使用独立AOT Canvas/Scaler和UIUpdate.cs，复用统一缩放策略，不走资源加载或Game绑定；GameRoot就绪后关闭启动Canvas。此界面单独校验AOT引用闭包，不套用UIViewDefinition窗口的Bridge要求。
- 正式运行只由Bootstrap场景保留一个EventSystem，窗口Prefab不自带；独立UIEdit预览场景也只放一个。
- 窗口有一个CanvasGroup和一个必要的GraphicRaycaster；普通装饰容器不逐层新增Canvas/Raycaster。
- Static/Dynamic用于编辑组织；只有有性能依据时才增加子Canvas，不把“每组一个Canvas”变成强制规则。
- PoolNode默认不显示且不接收射线；公共列表项模板可单独Prefab引用，不复制整套窗口宿主。
- 子组件复用统一布局和UIBindings引用表，不挂UIFormLogic/UIViewBridge，也不登记为主窗口；保留嵌套Prefab连接，改公共按钮/图标优先改源Prefab或Variant。

## 4. 命名与绑定

同一窗口用唯一UI名称贯穿代码、Prefab和配置，格式为`UI{功能名}`，例如UILogin。名称须匹配`^UI[A-Z][A-Za-z0-9]*$`；创建工具明确校验，不自动静默改名。功能目录仍为Game/Login、Content/UI/Login。

| 内容 | 命名规则 | 登录示例 |
| --- | --- | --- |
| 手写页面类及文件 | UI{Name}.cs | UILogin.cs，partial class UILogin |
| 自动绑定文件 | UI{Name}.G.Bindings.cs | Generated/UILogin.G.Bindings.cs，同一partial class |
| 可选页面状态 | UI{Name}.State.cs | UILogin.State.cs |
| 窗口Prefab及根节点 | UI{Name}.prefab / UI{Name} | UILogin.prefab / UILogin |
| UIViewDefinition配置 | UI{Name}.Config.asset | UILogin.Config.asset，ViewID=UILogin |
| 双布局配置 | UI{Name}.Layout.asset | UILogin.Layout.asset |
| 文案配置 | UI{Name}.Text.asset | UILogin.Text.asset |
| 窗口测试 | UI{Name}Tests.cs | UILoginTests.cs |

启动更新界面同样使用UIUpdate.cs/UIUpdate.prefab；GameFramework、GameRoot等基础组件容器按用途命名。主窗口名称和ViewID一致，改名通过Unity保留GUID，并在同一Task同步配置、生成物和引用。

- 名称表达用途，使用PascalCase，例如AccountInput、LoginButton、CancelButton、StatusLabel、VersionLabel、Background、MaskButton。
- 自研类型、接口、属性、方法、文件和资源名称中的UI统一大写，如IUIService、UIViewBridge、UIBindingGenerator。
- 同一UIBindings表内BindingID唯一且稳定，是合法C#标识符；不允许空格、重复、大小写冲突或C#关键字。列表用显式有序数组，不依赖Hierarchy当前遍历顺序。
- 使用Inspector拖拽“BindingID + 期望组件类型 + Component引用”，单项通过Resolve访问，显式有序数组通过ResolveMany访问。节点名称可作为首次建议，运行时按序列化引用解析，不使用Transform.Find或GameObject.Find定位页面控件。
- 只暴露程序确实使用的控件；纯装饰无需全部导出。改层级或重命名节点后，已有BindingID不随意变化。
- 手写文件和生成文件处于同一namespace及Client.Game程序集，均声明`sealed partial class UILogin`等同一普通C#类型。UILogin.cs承担页面行为，生成partial只持强类型控件引用和`BindComponents(UIBindings bindings)`/`ClearBindings()`，不生成另一套ViewBindings类或业务事件代码。
- Bind先完成BindComponents及类型校验，再订阅事件；Unbind先取消异步和事件，再ClearBindings，允许重复调用。生成文件带auto-generated标记。主窗口从UIViewBridge.Bindings取得引用表，子控件直接使用自身UIBindings。
- Prefab只挂Framework中的稳定AOT Bridge及Unity组件；UI{Name}、State和生成partial均位于Game，不序列化热更MonoBehaviour或热更类型引用。
- 重生成前先校验全部条目，失败不写半份文件；输出路径限制在当前窗口Game目录。字段移除/改名需在同Task修调用方并验证编译。

## 5. 编辑器入口

由Task U4提供以下菜单；首期只实现制作登录页需要的能力：

| 菜单/操作 | 作用 |
| --- | --- |
| Game/UI/Create Window | 输入窗口名与目录，生成标准Prefab及UIViewDefinition，已有目标不覆盖 |
| Game/UI/Config | 集中编辑ViewID、Prefab、Group、模态与遮罩关闭策略、LayoutDefinition |
| UIBindings Inspector / Add Selected | 把选中的明确类型组件加入引用表，支持定位、删除、重排和数组 |
| Game/UI/Generate Bindings | 校验后生成强类型C#绑定，保留手写文件 |
| Game/UI/Validate Selected | 输出所选窗口及其引用的公共控件的错误/警告，定位具体对象 |
| Game/UI/Preview | 打开UIEdit场景，加载选中Prefab和布局，不启动更新或连接服务器 |

编辑器修改使用Undo、SerializedObject/PrefabUtility和正确Dirty标记。保存UI配置和Prefab均需明确操作，不在普通查看Inspector时偷偷改资产。

## 6. 配置、事件和显示生命周期

- 主窗口登记唯一ViewID、Prefab资源地址、Group、模态策略和布局定义；地址由GF资源配置解析，不把名称猜成物理路径。
- Framework不静态引用Game的UI{Name}类型；Game.Entry创建UILogin等普通C#页面对象，打开对应ViewID并调用其Bind。
- 事件在Bind/打开时注册，Unbind/关闭时对称移除；池化重开后不得多注册一次。Prefab持久OnClick不绑定热更方法。
- 非交互文本、装饰Image关闭Raycast Target；按钮点击区和模态遮罩按用途保留。模态遮罩覆盖有效窗口区域并阻挡后层输入，“点遮罩关闭”由配置决定。
- 输入/滚动手势与按钮不互相误触；登录请求中禁重复提交并允许取消。引用异步结果时检查页面代次，关闭后不刷新旧页面。
- 显示/关闭交给IUIService与CanvasGroup生命周期；不由页面脚本随意改全局排序、相机或把根scale设0隐藏。

## 7. 横竖屏、字体与资源制作

- 竖屏参考1080×1920，横屏1920×1080，match=0.5；使用U1屏幕快照和U2布局配置统一驱动。
- 背景满屏，按钮/输入/正文位于SafeArea；左右旋转安全区不同也必须正确。
- 同一页面实例切换双布局，保留文本、焦点和在途状态；不做两套互相复制的业务脚本。
- 位置依赖锚点和布局约束，避免只对一个分辨率调绝对坐标。短屏和软键盘打开时主操作可滚到可见位置。
- 文本使用项目TMP字体资产，明确换行/溢出策略；测试中文、英文、长错误和数字，不用持续缩到不可读来掩盖布局问题。
- 图像复用公共Sprite/Atlas，九宫格背景使用正确border；不复制大图充当纯色底，不为每个小元素建独立材质。纹理压缩与Atlas实际打包设置按目标平台验证。
- 粒子、复杂动画、3D模型和高级按钮特效后续按实际需求迁移；首期使用标准Button加必要的通用反馈，不引入Lua按钮或旧SDK依赖。

## 8. 校验等级与交付检查

登记主窗口的阻断错误：缺失UIViewBridge/主Canvas/CanvasGroup、根挂Canvas、窗口自带EventSystem、重复ViewID或BindingID、空/越界引用、类型不匹配、非法C#字段名、生成物漂移、脚本Missing、热更类型被序列化、必要字体缺失、无效资源地址、Preview场景进入Player。Bootstrap的UIUpdate按上面的独立启动规则校验。

警告：装饰对象接收射线、额外Canvas/Raycaster、未采用锚点/布局的固定坐标、公共组件被Unpack、文本溢出和非必要材质副本。警告必须有具体对象与理由，不因旧工程历史资产问题阻断新窗口；构建检查只覆盖实际参与构建窗口及其依赖。

每个窗口交付前逐项确认：Prefab/配置已保存；绑定生成与编译通过；引用校验无错误；横竖屏和安全区矩阵通过；键盘/短屏主操作可达；打开关闭20次无重复回调；模态输入正确；需要发布的资源与热更代码在同一release集合。

## 9. Task映射

- U1/U2：屏幕快照、统一缩放、安全区和双布局。
- U3：UIViewBridge、IUIService与绑定生命周期。
- U4：模板、配置、Inspector、C#生成、校验和UIEdit预览场景。
- U5：按本规范制作登录页并验证异步/事件行为。
- D1/D2：构建阻断检查、实际窗口闭包和Android/Windows设备验收。

本规范是新项目选择。旧项目明确规则、样例习惯和新设计已分别说明，不把全部旧检查器/素材/Editor脚本整包复制。
