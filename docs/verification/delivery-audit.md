# 当前交付核对表

范围修订：`2026-11-01-portrait-interface-only`。整体未完成，当前19/21 Task；不能据此表宣称首期全部完成。

| 要求 | 当前证据与结论 |
| --- | --- |
| C#框架、GF资源/UI、HybridCLR热更新、事务与回退 | 19项实现任务已有跟踪与复核记录；当前D1归档见 `d1-build-acceptance.md`，Windows/Android原PlayerBuildId分别为0bdb3aa994954531b0432eafa17ede00、14a355dc03454f6aab5b13d077e67650。 |
| 同主包代码/资源更新、Protobuf新增Parser、故障拒绝与下进程回退 | 两平台核心22项均具备原始通过记录；Windows完整24项正式命令退出0。Android26项源数据导入通过，但正式命令因两项NotRun退出1。 |
| 竖屏720×1280、1080×1920 | Windows真实窗口尺寸与编辑连续性通过；Android在对应设备像素配置启动后验证实际尺寸、稳定布局、安全边界和可编辑输入通过。 |
| Android后台与安全区 | 新连续数据集通过，安装次数1；真实Home/返回同一Activity自动恢复编辑状态，未重新点击掩盖恢复结果。 |
| Android键盘 | 真实开关已记录，文本保持；系统返回收起后Unity结束编辑并复位非编辑态光标，现规则拒绝。等待用户确认期望结束编辑或保持编辑态，未放宽判定。 |
| Android竖屏滚动 | 尚未通过。Windows窗口工具无法激活雷电、截图黑屏，未收到实际滚动输入；失败现场保存，进程已正常结束。 |
| Protobuf共享来源与接口 | 可编辑源位于comm/proto，客户端为生成C#；干净checkout协议契约24项、生成器28项与两个生成文件字节核对通过。保留会话接口，本期不验证真实登录服务器。 |
| 目录和运行时边界 | Framework/Game当前87个C#文件，无UnityEditor、绝对Windows盘符或Lua/XLua引用；官方HybridCLR之外无自研Packages程序集。目录/迁移规范见project-layout和migration-ledger。 |
| 必要注释 | 已覆盖启动事务不可逆边界、完整slot、程序集单次加载、UI/资源所有权、GF回调回主线程、后台编辑恢复等关键不变量；未为注释改动冻结的运行时代码。 |
| 干净工程复现 | 独立checkout完成依赖恢复、协议生成/测试、Unity F1 Edit5/Play1、标准Windows IL2CPP构建与原生登录启动，构建/进程退出0且原生文件未变。 |
| 工具整合 | 主工程已整合当前工具/测试/计划；20套366项回归输入字节核对一致。运行时C#、资源、ProjectSettings、Packages无分歧。 |
| 最终复核与完成标记 | 两项Android验收处理后，仍需完整28项聚合、最终独立整体验收复核、D2/D3关闭及计划总勾选。 |

## 原始证据入口

- Windows：`E:/sgame/Builds/D2/W-20261101T002710348Z/exports/portrait-acceptance-20261101T063425325Z/formal-verification/framework-result.json`。
- Android：`E:/sgame/Builds/D2/P-20261101T064919739Z/exports/portrait-partial-20261101T113649860Z/partial-formal-verification/framework-result.json`。
- 工具回归：`E:/sgame/.worktrees/client-release-verifier/Builds/Tests/D2/ToolRegression-20261101T093939394Z/result.json`。
- 干净复现：`E:/sgame/Builds/D3/CleanCheckout-20261101T002117166Z/protocol-reproduction.json`。
- 边界复核：`E:/sgame/.worktrees/client-release-verifier/Builds/Tests/D3/FinalBoundary-20261101T075009601Z/result.json`。

横屏、旋转、其他尺寸专项、真实登录服务器验收已由用户移出首期；iOS及真实ARM硬件/性能继续NotRun。三国登录图外观改造仍为后续独立待办，没有计入本Goal的已完成声明。
