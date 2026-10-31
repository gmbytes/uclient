# D1 统一构建与原生基线验收

> 当前状态：695dee2 修复了热入口异常被清理关闭遮蔽的问题（完整413/142回归通过）。该改动涉及固定层，本文原Native通过记录保留历史身份；D1需按修后新Player重新验收，D2旧21项不能混入新基线。

状态：已完成。此记录只关闭D1，D2综合故障/UI/会话矩阵及D3干净工程复现仍未完成。

## 当前正式产物

| 平台 | PlayerBuildId | 主包输出 | V2补丁输出 |
| --- | --- | --- | --- |
| Windows64 | a019389c3f5342b1be60c06c1c2090a5 | E:/sgame/Builds/D1WindowsLocalhost/Build_20261031T171435889Z_defa7e9a89b6 | E:/sgame/Builds/D1WindowsLocalhost/Build_20261031T171843848Z_757a3924da59 |
| Android | 31a7aaaf1c0f45a49d82eed7eb0047da | E:/sgame/Builds/D1Final/Build_20261031T153023251Z_38d2234aecce | E:/sgame/Builds/D1Final/Build_20261031T153558742Z_93c876f70c40 |

Windows使用原生包内公开配置`http://localhost:18765/`；Android使用`http://127.0.0.1:18765/`及拥有的ADB反向映射。两者均为显式本地开发端点，未修改系统代理。Windows构建来源为c120d5e，Android来源为7665bf9；固定C#实现均包含ac44f88修复。两次构建之间后续变更只有工具/文档及Windows原生公开地址配置。所有后续补丁仍须选择对应平台的原始Archive。

## 逐项证据

- [x] 唯一CLI入口`ClientBuild.Run`及统一`Execute(BuildRequest)`；真实Editor菜单选择/执行已完成。证据：旧工作区`Builds/Tests/D1/EditorMenu-Patch-Android-20261031T062229352Z`，实际菜单结果Succeeded、唯一实际子目录、七引用相等、Editor退出0且源码恢复。之后构建入口C#没有行为变更。
- [x] 契约、互斥、热程序集排除、UI合规、错误路径和源码/场景恢复：最终字面命令`./Tool/Client/Test-Client.ps1 -Task D1 -Mode EditMode`为70/70、零失败/跳过、退出0。证据：`Builds/Tests/D1/20261031T172555370Z-87d256bf`；外层`FinalTaskGate-20261031T172554400Z`源码前后完全一致。此前完整Edit413/413、图形Play140/140覆盖相同固定C#源码的启动/退出修复；定向行为RED和失败记录均保留。
- [x] 完整Player构建、固定点/最终裁剪与不可变归档：Windows `Builds/Tests/D1/WindowsLocalhost-20261031T171423400Z`三次构建（Player、V2、Throw）成功，源码恢复且原Archive逐文件前后相等。Android `CanonicalFinal-20261031T152623672Z`对应Player/Patch成功、恢复和归档不变。
- [x] 独立Patch严格复用原始元数据和七固定引用：Windows59份、Android48份元数据逐文件与原归档一致，七引用完整SHA一致；官方兼容检查通过。Windows两个实际PE为AMD64/PE32+，Android APK仅含三份AArch64原生库，签名校验通过；实际清单含正确包名和INTERNET权限。
- [x] 同主包真实新进程执行V1/V2：Windows PID79604/41008、原生attempt `b3fa378d8b7848e48f3ee465eb7653f6`/`0230fbc46a224f18a2f2b72539a7a5eb`；Android PID1982/2228、V2 attempt `0259042fa6864af98f6ce5881a34ffca`。每次一个资源根、两个热程序集、全部原元数据OK、零未预期原生异常及启动失败，最终Ready；主包文件前后不变。
- [x] 新协议类型与资源实际变化：两平台最终V1 DLL中无AddedMessage，V2 DLL有Parser；V2原生日志执行list/map/unknown-fields/uint64/hot-generics/empty-registry探针，页面显示Framework v2。实际720×1280 Windows客户区、可调整窗口和DPI感知观察已记录；两平台CJK页面截图可读。
- [x] 普通Player隔离：两平台最终AOT元数据均无Game热程序集、测试程序集或TestRunner；Bootstrap.Proof只保留Unity生成的空程序集类型，没有HotfixProof加载器。
- [x] Android原包、进程ABI和slot：安装APK SHA256始终为`97121e793f56041f0086ec877728b9ef03a8c105af46d4571d601eead1f6d758`，精确PID的ActivityManager为arm64-v8a/instructionSet arm64。V1停止后、V2启动前及V2停止后各捕获54文件，均与签名清单及原签名匹配。

Windows原始运行目录为`WindowsRuntime-d1-20261031T171956265-v1`和`WindowsRuntime-d1-20261031T172158935-v2`；Android为`AndroidRuntime-d1a-20261031T154442369-v1`和`AndroidRuntime-d1a-20261031T154609102-v2`，均在主工程`Builds/Tests/D1`下。每个目录保留原始事件、实际PID/终态、日志和截图；`runtime-audit.json`逐项通过。物理类型/PE/元数据/引用证明在WindowsLocalhost目录和Android原构建目录中；Android slot证据为`CallbackAndroidV1Slot-20261031`、`CallbackAndroidV2Slot-20261031`。

## 保留的边界与失败

旧Windows IP端点在当前系统代理环境返回502。隔离副本只改公开端点为等长localhost后下载成功，验证了配置方向；副本是DiagnosticOnly，未当作正式验收。随后正式生成了上述新Windows Player和独立补丁并重跑V1/V2。旧d967、9a96等产物和日志保留历史身份，不替代当前产物。

雷电是用户选择的Android ARM64/Houdini功能验收环境，设备主ABI为x86_64；真实ARM硬件性能/兼容和iOS仍未验证。D1不证明完整候选故障/强杀/UI/输入法/会话矩阵，也不证明D3的干净checkout交付或生产认证。
