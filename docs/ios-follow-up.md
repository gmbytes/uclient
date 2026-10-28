# iOS 后续接入与验收清单

iOS 不属于首期验收。当前首期目标是 Windows x64 与雷电环境中的 Android ARM64 APK；实际状态见[实施进度](implementation-progress.md)。下列 iOS 项目均为 **NotRun**，不表示已经支持，也不构成分发或上架承诺。

自研运行时代码继续使用 C#。iOS 接入沿用同一套稳定契约、GF Package、签名发布、完整版本目录和试启动流程，并为 iOS 创建独立 Player 与元数据归档。

## 当前明确缺少的适配

- `DiskBudget` 的默认原生容量查询目前只实现 Android 和 Windows；其他平台抛出 `PlatformNotSupportedException`。
- `AotMetadataLoader` 的实际运行平台识别目前只接受 Android 和 Windows Player。
- `PlayerBaselineArchive.Stage` 目前只接受 `BuildTarget.Android` 与 `BuildTarget.StandaloneWindows64`；构建入口也限定这两个首期目标。
- 当前没有 iOS Player、Xcode 工程、签名产物、原始 AOT 元数据或设备运行证据。

后续需完成这些适配及其验证，保留现有拒绝不匹配平台、PlayerBuildId 和元数据集的门禁。

## 后续执行顺序

| 项目 | 必须完成并保存的证据 | 状态 |
| --- | --- | --- |
| Mac 与工具链 | 可用 Mac、确切 macOS/Unity/HybridCLR/Xcode/SDK 版本及兼容性核对；在执行时依据当时官方支持范围选择版本 | NotRun |
| 签名与设备 | 明确团队、Bundle ID、证书/描述文件与可用 ARM64 iPhone/iPad；凭据和私钥留在受控本地配置，记录签名身份与设备环境 | NotRun |
| 平台构建入口 | 为统一构建请求、预检、元数据运行平台和不可变归档增加显式 iOS 目标；验证不影响 Android/Windows，失败仍恢复源设置 | NotRun |
| 存储与包内基线 | 实现并实测 iOS 容量查询；验证应用包读取、持久目录、同卷改名、状态替换/备份、清理与中断恢复 | NotRun |
| Release IL2CPP 主包 | 使用实际 ARM64、非 Development 的 Player，保存完整命令、工具/锁/裁剪配置、原生文件及签名哈希 | NotRun |
| 原始 AOT 归档 | 对该 iOS Player 完成有界生成固定点和最终 post-strip 逐字节比对；归档独立 PlayerBuildId/metadataSetId、原始元数据及编译输入 | NotRun |
| 补丁兼容门禁 | 从该原始 iOS 归档构建补丁；实际运行官方缺失 AOT 检查，拒绝库/契约/信封改变、错误平台、缺文件及修改后的元数据 | NotRun |
| 签名与网络 | 在实际 Player 中验证固定公钥、原始字节验签、HTTPS/TLS、开发端点配置、网络失败及下载取消；保存应用包网络配置和请求结果 | NotRun |
| 本地就绪 | 使用正式 `ConfigureLaunch`→`LaunchAsync` 路径，经过实际 Package 资源、元数据、热入口、Protobuf 自检和登录首帧后提交 known-good | NotRun |
| 同主包 V1→V2 | 同一个已安装主包加载新增 C# 类型/分支及更新后的 GF UI 资源；不重新安装，保存主包/原生哈希、进程、release、可执行标记和画面 | NotRun |
| 坏版与下进程回退 | 实测坏签名/hash/身份、缺 DLL/Bundle、坏版本表、空间不足、取消/断网、关键写入点强杀和 ThrowOnStart；同进程不再切根或二次加载，下一进程回到可用版本 | NotRun |
| 更新端点不可用 | 停止受控更新端点后进行真实失败请求，再从本地 known-good 启动；记录网络失败及本地资源来源 | NotRun |
| UI 与系统生命周期 | 实测竖屏、左右横屏、安全区、软键盘、短窗滚动、返回/关闭、后台恢复；同窗口/Presenter 保留输入、焦点和在途请求，记录布局稳定帧 | NotRun |
| Protobuf 与 Tok | 在实际 ARM64 Player 中执行集合/Parser/未知字段/uint64 边界，并通过真实开发 Tok 验证成功、拒绝、取消和断线；服务器错误不隔离健康补丁 | NotRun |
| 分发与审核 | 按届时适用的分发渠道、签名和审核要求核对热更新行为；任何实际上传或发布另按明确授权执行 | NotRun |
| 独立复核 | 运行 iOS 对应的完整故障/UI/运行矩阵，核对本次源码、原生产物及进程证据；未执行 required case 保持 NotRun 并使总验收失败 | NotRun |

## 归档与完成边界

iOS 的 ARM64 与 Android 的 ARM64 分属不同平台，不能互用 Player 或 AOT 元数据。补丁必须引用所选 iOS 原始归档；修改引擎、稳定契约、固定信封、原生库或 AOT 组成后需要新 Player。

程序集与资源根仍以进程为边界：坏版在下一次完整启动恢复，不增加运行中卸载程序集或二次切换资源根的承诺。首次 iOS 完成声明必须依据该平台独立的原始证据，不能使用 Editor、Android/Windows 或构建成功替代实际设备运行。

上述清单完成并经复核后，再更新平台支持范围与验收台账。首期文档继续保留 iOS 未验收的状态。
