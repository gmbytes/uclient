# 客户端框架实施进度

开始日期：2026-10-29。目标为完整实现[总计划](superpowers/plans/2026-10-28-csharp-hotupdate-framework.md)并完成相应检测；当前未完成整个目标。

## 当前状态

| Task | 状态 | 证据/下一项 |
| --- | --- | --- |
| N0 | 已验证并通过独立审查 | 24/24输入用例；client c5f5a79、comm467e131 |
| F1 | 实施中 | Unity项目创建成功；导入、测试入口及平台smoke构建进行中 |
| F2–F8 | 待实施 | 按依赖推进，原生Player热更新验证不可省略 |
| N1–N4 | 待实施 | C#生成、分帧、登录和Tok互操作尚未完成 |
| U1–U5 | 待实施 | 自适配、绑定、制作工具和UILogin尚未完成 |
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
- AndroidSDK/NDK/JDK仍在准备；尚未进行Android构建或实机验证。
- 锁定UGF官方提交`e7eb3ef11393df0ac504204c480cdb839752b05f`；HybridCLR包v9.0.0为`86e6407a35d7da0b7697f055c7488ef859c51ea4`。
- HybridCLR v9实际输出目录由SettingsUtil决定，当前源码使用`Library/HybridCLR`；构建不硬编码旧HybridCLRData路径，基线归档仍须按平台及PlayerBuildId保存。
- 沙箱Unity启动无法连接本机授权IPC；经允许的非沙箱Unity调用已成功解析现有授权并创建项目。没有修改或重新激活授权。

所有后续完成声明必须附当前实现对应的实际证据。Android ARM64实机、Windows x64 IL2CPP和完整热更新/回退仍为最终必需门槛；iOS不在首期验收内。
