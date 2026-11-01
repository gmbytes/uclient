# 主包、补丁与回退操作说明

> 当前状态：后台编辑态修复后的Windows/Android新基线已完成D1复验（413/149完整回归、D1 70/70、原生同主包V1/V2与Throw回退、Android后台恢复资格检测）。当前产物见D1记录；完整D2矩阵和D3仍未完成，旧基线案例不混入新数据集。

本文说明已实现的发布边界与客户端恢复行为。F8 的 Windows Release IL2CPP 和雷电 ARM64 APK 技术闭环已经通过；D1 正式构建入口与当前两平台最终归档已通过验收，见[D1验收记录](verification/d1-build-acceptance.md)；D2/D3 综合验收仍在推进。实际状态见[实施进度](implementation-progress.md)。

## 先选择交付类型

| 变化 | 交付方式 | 必须满足的边界 |
| --- | --- | --- |
| 引擎、原生 SDK、稳定契约、固定 Tok 信封、AOT 组成或固定库/裁剪输入变化 | 新 Player | 创建新的平台基线与 PlayerBuildId，重新归档该主包的原始元数据和原生文件 |
| 允许的 `Client.Game`/`Client.Game.Protocol` 代码变化 | 原基线补丁 | 官方兼容检查通过，七个固定编译引用完整 hash 与原归档一致 |
| 登录 UI、布局、文案或其他可更新资源变化 | 原基线补丁 | 生成完整 GF Package 快照，目录、布局与 Prefab 使用一致资源图 |
| 让客户端选择一个已发布的兼容版本 | 签名渠道变更 | 渠道声明的 releaseId、清单 hash、签名和包内身份全部匹配；目标未被本地隔离 |

首期正式业务协议准入仍为空。已有 Tok 信封和开发登录可用于框架示例；隔离测试 Parser 不代表已迁入游戏业务。目录与程序集边界见[运行时架构](runtime-architecture.md)和[Tok 接入契约](tok-demo-protocol.md)。

## 新 Player

1. 固定 Unity、HybridCLR、第三方依赖、目标平台、架构、稳定契约和信封来源锁。Windows 使用 x64 Release IL2CPP；当前 Android 使用 ARM64 Release IL2CPP。
2. 在 HybridCLR 生成前冻结实际编译的启动代码、信任材料与公开配置。对官方生成过程取得有界固定点，保留生成、编译、裁剪和 bridge 输入的原始字节。
3. `PlayerBaselineArchive.Stage` 产生 PlayerBuildId、metadataSetId 和待完成归档；这些身份作为构建拥有的包内数据写入最终 Player。
4. 构建最终 Player，再将最终 post-strip 元数据与包内元数据逐文件比较。`Finalize/Open` 完成原始归档；保存主包、原生库、工具锁、配置和兼容检查结果。
5. 在该主包上验证本地基线、补丁启动和回退。构建成功或存在 `pending.json` 都不等于基线归档和运行验收成功。

Android 与 Windows 归档分别保存。补丁身份必须匹配原包的 PlayerBuildId、平台、架构、Unity、契约、信封和 metadataSetId。不能把一个平台的元数据或另一个 Player 的归档混用。

## 从原基线制作补丁

1. 显式选择原始、已完成的 Player 归档，保留其内容和原生 hash。
2. 从共享 `comm/proto` 生成所需协议代码，编译目标热更 DLL。固定信封改变应转为新 Player；当前正式业务 Registry 仍为空。
3. 对原归档执行官方 MissingMetadataChecker 和固定引用 hash 检查。缺少 AOT API、固定引用改变或身份不匹配时，结果应要求新 Player。
4. 构建完整 GF Package 资源快照，组合热 DLL、原归档元数据、独立 facade 和配置。补丁流程不能重新生成元数据后替换原归档。
5. 对每个文件记录长度和 SHA-256，再签名 release 清单；签名渠道指针固定该清单的原始字节 hash。清单和渠道都按原始字节验签，签名后不能再格式化 JSON。

每个 releaseId 对应不可变完整快照。`ReleaseStore` 拒绝以不同清单覆盖已经完整存在的同名 slot；修改后的发布使用新的 releaseId。客户端在完整下载、校验和同卷改名后才发布 Candidate，未完成的 `.partial` 目录不能成为启动版本。

正式验签使用包内受信任的 RSA-2048 公钥、SHA-256 和 PKCS#1 签名。默认信任列表为空并拒绝更新。F8 的测试私钥只留在忽略的运行产物中；D1 的正式公开配置与签名输入仍须由其构建流程验证。

## 发布与渠道回滚

先准备完整、不可变且通过兼容检查的 release，再提供匹配的签名渠道指针。客户端依次读取 `channel.json`、`channel.sig`、指针声明的 release 清单及其签名，并验证身份与文件内容。只改 URL 或 releaseId 而没有匹配签名/hash，不能得到可用更新。

发布回滚是把受信任的渠道重新指向一个兼容、完整且未被隔离的版本。它不会在正在运行的进程中卸载程序集或替换资源根。客户端需要完整重启，在新进程中检查、选择并启动该版本。当前工具交付范围是本地开发产物与测试端点；外部分发不属于已完成验收。

## 坏版的下进程回退

本地状态使用 KnownGood（K）、PreviousGood（P）、Candidate（C）、Attempt 和 BadReleaseIds。它们承担不同职责：

- C 通过完整性和兼容检查后才可以试启动。试启动前先持久化 Attempt。
- 实际资源初始化、元数据/热 DLL 加载、信封自检、真实登录首帧、当前代次 Ready 请求以及入口成功返回全部完成，才原子晋升为 K。
- C 失败或在晋升前被强杀，下个进程隔离 C，选择原来的 K，并保留 P。例如 C=v3、K=v2、P=v1 时，失败后回到 v2，v1 仍保留。
- K 自身启动失败时，再转向仍可用的 P。一个进程只激活一个资源根、尝试加载一组热程序集；失败后要求完整重启。

被隔离的 releaseId 不会因渠道再次提供它而自动重试。修复以新的 releaseId 发布，保留隔离记录。登录服务器拒绝、断线或用户取消属于会话结果，不能单独作为补丁损坏证据。本地 Ready 也不表示服务器认证成功。

准备阶段网络不可用时，可以启动已验证的本地 K 或包内基线。F8 的端点不可达验证停止了拥有的本地 HTTP 端点/反向映射，并保留真实失败请求；它没有把关闭 Wi-Fi 或断开所有网络写成已测。

## 证据与当前命令边界

F8 的可执行技术证明命令、当前双平台 runId、PlayerBuildId、结果与限制见[热更新验证](verification/hotupdate.md)。它会创建独立技术基线，不能替代 D1 的最终 Player/Patch 归档或 D2 的全部故障矩阵。

D1 完成后，本文还需加入实测的统一构建/补丁命令、返回的实际输出子目录、独立签名输入和最终原始归档示例。D3 需从干净 checkout 按最终 README 实际执行后才确认可复现。

每次验收至少关联实际 runId、平台、PlayerBuildId、metadataSetId、清单 hash、原生文件前后 hash、真实进程 PID、启动版本、持久状态、日志和画面。必测项未执行应标 NotRun，不能只依据目录存在、构建退出0或截图判通过。D2 仍需在最终主包上补齐故障/强杀、Protobuf 新 Parser、真实开发登录及 UI/输入法矩阵。

首期 Android 证据来自用户选择的雷电 ARM64/Houdini 环境。真实 ARM 硬件/性能和 iOS 保持未验收；iOS 后续项见[iOS 清单](ios-follow-up.md)。
