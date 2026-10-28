# 客户端框架实施进度

开始日期：2026-10-29。目标为完整实现[总计划](superpowers/plans/2026-10-28-csharp-hotupdate-framework.md)并完成相应检测；当前未完成整个目标。

## 当前状态

| Task | 状态 | 证据/下一项 |
| --- | --- | --- |
| N0 | 已验证并通过独立审查 | 24/24输入用例；client c5f5a79、comm467e131 |
| F1 | 已验证并通过独立审查 | EditMode5/5、PlayMode1/1、两平台实际IL2CPP编译、零测试拒绝；1f6cdc1/0799efc |
| F2 | 已验证并通过独立审查 | 9a2982c；真实GF目录注入、同Player冷A/B、发行隔离和快照不变 |
| F3 | 已验证并通过独立审查 | 7b58cec；11/11边界测试，同Player实际DLL v1/v2及新增类型执行 |
| F4 | 已验证并通过独立审查 | 4fadba6/e4ed378；严格JSON转义82/82，主工程合并回归154/154+39/39 |
| F5 | 已验证并通过独立审查 | a3440af/e1e6330；合并回归224/224+58/58，含F5的34+5项 |
| F6 | 已验证并通过独立审查 | cd02dc6/bb9659b；57项原生元数据与泛型/Parser通过，合并300/300+71/71 |
| F7 | 已验证并通过独立审查 | b29d0de/3ead85c；错误保留与取消清理修复，主工程332/332 Edit与123/123图形Play集成通过 |
| F8 | 实施准备 | 已完成只读构建/设备预检，接续两平台同主包更新与坏版回退；原生结果尚未通过 |
| N1 | 已验证并通过独立审查 | 3a894568/75516cd；生成器54/54、Unity22/22+4/4，实际Protocol→Game同Player证明 |
| N2 | 已验证并通过独立审查 | 8756a77/822b166；23/23+13/13，重连代次与超时发布竞态修复 |
| N3 | 已验证并通过独立审查 | 56c3d78/4d24c95；并发邮箱修复及主工程241/241+69/69通过 |
| N4 | 已验证并通过独立审查 | ccd714c/6d8e0f0、comm375d1dc/60ac62e；Rust互验、实际Tok3项及边界修复通过 |
| U1 | 已验证并通过独立审查 | 19a492a；统一屏幕快照，合并回归154/154+39/39通过 |
| U2 | 已验证并通过独立审查 | 6c85afb/f24163e；40组合矩阵、键盘手动滚动及恢复，合并回归通过 |
| U3 | 已验证并通过独立审查 | 27a0b7f/430a170；合规层级16+10项及合并回归通过 |
| U4 | 已验证并通过独立审查 | 8212d63/43093f8；工具/预览/满屏背景与局部安全区，合并回归通过 |
| U5 | 已验证并通过独立审查 | c752806/b55dbc1/639f447；完整310/95，主工程9项Edit及16项真实图形集成通过 |
| D1–D3 | 待实施 | 最终基线、故障/设备验收及交付文档尚未完成 |

## N0 验证

- 完整Tok v3信封原始SHA256：`907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583`。
- 实际Tok来源提交：`f550195d60577524e3272dda2789f07ab381d7c1`；协议源和许可均逐字节核对。
- 行为RED：无输入校验版本错误接受20种非法输入，测试2/22通过；补上校验后GREEN24/24通过，包含缺源、字段/oneof漂移、错误来源锁、非法准入和工作区源码漂移。
- 当前测试命令：`pwsh -NoLogo -NoProfile -File Tool/Client/Tests/Test-TokProtocol.ps1`。
- 本次证据：`Builds/Tests/N0/green-final/suite-result.json`，Total24、Failed0；另有直接校验的TASK_PASS N0结果。
- 独立审查确认规范和质量均通过，未重跑相同测试。N0不证明C#生成、网络互通、登录或热更新完成。

## 工具链与执行决定

- 客户端独立Git仓库，分支`feat/csharp-hotupdate-framework`；comm沿用既有feature分支。保留comm原有未暂存删除`proto/game/v1/README.md`。
- Unity2022.3.25f1（530ae0ba3889）、VS2022 C++与.NET SDK已检测存在。
- 官方Windows IL2CPP模块下载完成，MD5为`1722a2a6faa63299520be664a59dbcd3`，签名有效；安装退出0，已发现win64_player_development_il2cpp和win64_player_nondevelopment_il2cpp。
- AndroidSDK32/BuildTools32、NDKr23b、JDK11已安装并验证；Android实际ARM64 IL2CPP构建已通过，APK仅含arm64-v8a，ELF64/AArch64。构建目录Builds/Player/Android/20261028T172219655Z-931b98d6。
- 锁定UGF官方提交`e7eb3ef11393df0ac504204c480cdb839752b05f`；HybridCLR包v9.0.0为`86e6407a35d7da0b7697f055c7488ef859c51ea4`。
- HybridCLR v9实际输出目录由SettingsUtil决定，当前源码使用`Library/HybridCLR`；构建不硬编码旧HybridCLRData路径，基线归档仍须按平台及PlayerBuildId保存。
- 沙箱Unity启动无法连接本机授权IPC；经允许的非沙箱Unity调用已成功解析现有授权并创建项目。没有修改或重新激活授权。
- F1最终证据：EditMode5/5在Builds/Tests/F1/20261028T173250580Z-182f79bc，PlayMode1/1在20261028T173344232Z-d4987267；Windows IL2CPP在Builds/Player/Windows64/20261028T172511945Z-5277da5c。构建errors0/warnings1，官方“尚无热更模块”警告保留，不加假模块消警告。
- Android APK实际11,363,987字节，SHA25681694fb5606e09d3ef30502474dc832a4abb91568d977b6f4552662fd3efc94c；Unity总构建大小是不同指标。雷电运行15秒仍存活，包及日志确认arm64-v8a/il2cpp，经Houdini转译；空Bootstrap截图和图形诊断已保留，不作为UI验收。

用户已选择并启动雷电进行当前Android验证：emulator-5554，Android14/API34，x86_64主ABI、arm64-v8a支持、libhoudini。后续在此运行ARM64 APK，记录转译限制；真实ARM硬件兼容/性能和iOS保持后续未测。

F2最终证据为Builds/ResourceSlots/20261028T175002272Z-eedff18c：同EXE/GameAssembly两次冷启动均自然退出0，分别读取原生Package中的A/B marker，根路径和3个加载代理正确，晚改根/二次激活拒绝，两套Resources与inactive Scratch前后快照不变。F2 PlayMode3/3、实际无proof define的普通Player脚本编译隔离1/1、F1回归6/6、N0 24/24；独立审查通过。该证明限Windows单marker原型，不代替Android复杂资产或热更/回滚验收。

F3最终证据为Builds/MinimalHotfix/20261028T180115921Z-f26931b8：v1/v2真实Client.Game DLL哈希不同，v2新增类型执行Compute输出proof-v2:42，两个进程均自然退出0；347个原生文件在两次运行前后及当前逐文件一致。48份元数据与这次最终Player的Managed备份字节相同，两进程48个返回码均OK。普通Player实际C++输出无proof标识、无Game AOT；F1/F2回归和原生资源A/B重跑通过。独立审查确认规范/质量通过；此项只证明Windows最小DLL替换，签名、下载、回退、Android和完整协议类型仍待后续。

N1生成器审查曾发现命名空间字段存在性、protoc数字命名、CLR类型冲突和保留文件发布校验4项缺陷；5b6d4d1已逐项RED→GREEN修复，完整50/50。独立复审核对真实生成/编译/执行、信封check内容/时间戳不变及真实NTFS发布回滚证据，4项全解决。Unity/AOT程序集、正式运行库和Player集成继续单独验证，不把.NET测试当作IL2CPP证明。

所有完成声明必须附当前实现证据。Android ARM64 APK在选定模拟器中的功能验收、Windows x64 IL2CPP、完整热更新/回退仍为首期必需门槛；不能用Editor/Mono或只有构建成功代替。

## F7 启动事务与主工程整合

F7 Part B 在独立工作区390824e/bacd292完成；首轮审查发现错误证据在回退时被清除、启动取消未完整释放拥有的上下文与下载监听，两项均以行为RED复现并修复，限定复审确认2/2解决、无新增问题。错误记录独立于当前显示阶段保留，只有捕获的selection/context/epoch就绪请求能提交；无调用者代次的字符串Ready接口拒绝提交。

最终独立工作区全量322项EditMode、107项非批处理图形PlayMode通过，1302个文件哈希一致。主工程保留实际U5登录页面/Entry、协议与网络修复，无冲突整合为b29d0de/3ead85c；全量332项EditMode和123项图形PlayMode均通过、零跳过、Unity退出0，1340个源文件在两次运行及当前一致。证据分别在Builds/Tests/Integration/F7-U5/20261028T222657015Z-EditMode与20261028T222843571Z-PlayMode；source-comparison.json保存哈希比较。生成的PackageManagerSettings仅在进程结束后归档并移除，工程保持干净。

这些结果证明Editor中的启动事务、真实UI帧与集成行为，不代替最终Release IL2CPP主包。F8仍须新建Windows与Android ARM64原生基线，完成同APK/EXE的v1→v2代码及资源更新、ThrowOnStart下进程回退和更新端点不可达启动；D1–D3最终归档、完整设备/故障矩阵和干净checkout交付尚未完成。通过日志中的Unity授权/SSL环境诊断仍保留为已知限制，不称为无噪声日志。
