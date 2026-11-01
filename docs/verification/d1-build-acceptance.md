# D1 统一构建与原生基线验收

> 当前状态：首期框架21/21 Task已完成并通过最终独立审查。Windows24/24、雷电Android28/28正式原生验收通过，工具回归382/382；当前PlayerBuildId与证据见[D1产物](d1-build-acceptance.md)和[最终交付核对](delivery-audit.md)。iOS、真实ARM硬件/性能、生产接入及登录美术改造仍属后续。

状态：已完成。此记录证明D1；D2/D3的最终结果分别见framework-acceptance与delivery-audit，旧基线与失败案例按历史身份保留。

## 当前正式产物：后台编辑状态修复

运行时产品代码来源为 `a8619430205e2bcd6e0fc0a34065392a50a82306`；其后的 `3e2f3b9` 只增加GF集成测试与进度记录。完整回归413/149、D1定向70/70均通过。产物根目录为 `E:/sgame/Builds/LateFocusFixed`。

| 平台 | PlayerBuildId | Player目录 | V2目录 | Throw目录 |
| --- | --- | --- | --- | --- |
| Windows64 | 0bdb3aa994954531b0432eafa17ede00 | Build_20261031T232609241Z_1255ba548665 | Build_20261031T233002998Z_791e8938c247 | Build_20261031T233113208Z_c2f1049bdb92 |
| Android | 14a355dc03454f6aab5b13d077e67650 | Build_20261031T230243829Z_23745d0b666b | Build_20261031T232457671Z_1ad310e8265f | Build_20261031T232533222Z_29c05540c8e6 |

- [x] 六次实际构建成功，源码恢复一致，补丁前后原始归档未变；Android先构建主包并完成原生焦点资格检测，再显式复用该原归档出补丁。检查结果位于 `Builds/Tests/D2/LateFocusFixed-20261031T230234042Z` 和 `LateFocusRemaining-20261031T232446423Z`。
- [x] 两平台全部原归档文件、PE/ELF机器头、7固定编译引用、59/48原始元数据、V1新增类型缺席/V2 Parser存在、普通Player测试/热程序集隔离及Throw协议完全复用V2均通过。证据为后一个目录的 `physical-compatibility.json`。
- [x] Windows同一主包V1 PID81848、V2 PID70872；Android同一APK V1 PID20371、V2 PID20564。四次全部Ready、单资源根/两个热程序集、原元数据全部OK、新Parser真实执行，零未预期错误/失败，原生文件不变。原始目录依次为 `WindowsRuntime-d1-20261031T233325234-v1`、`WindowsRuntime-d1-20261031T233508224-v2`、`AndroidRuntime-d1a-20261031T234053956-v1`、`AndroidRuntime-d1a-20261031T234233422-v2`，均在主工程 `Builds/Tests/D1` 下，各有严格审计结果。
- [x] Android原APK SHA256为 `9104b0f1ecb2674e0e947197a18196f1b50b02588ec1ee6e6e8988315fb2e32b`，安装前核对并备份旧测试APK，仅保留数据安装；签名和三个AArch64库验证通过。V1与实际V2槽位各54文件及原始清单/签名一致。
- [x] Throw探针：Windows49848→15508、Android20793→21054，原始 `HotfixInitializationFailed`/`Hotfix`/`D2 Delivery ThrowOnStart` 保留，坏版隔离，无Ready；新进程从v2恢复，旧坏版记录保留。两平台 `native-fix-probe.json` 只标资格探针，不计完整D2用例。
- [x] 后台焦点原生资格检测：Android PID19413、同View/Input实例和文本哈希，Home→同Activity恢复后自动恢复焦点/光标/选区，无重新点击。完整记录、原始事件和前后截图位于 `E:/sgame/Builds/FocusLifecycleFixedTests/focus-20261031T231724830Z`。平台临时停用键盘期间允许非编辑态选区重置；所有文本/控件身份、每个实际编辑态样本及最终稳定编辑态仍严格匹配。

D1定向结果为 `Builds/Tests/D1/20261031T233431843Z-774431c3`。当前两平台原生进程与D1本地服务均已结束，拥有的18765反向映射已移除；持有ADB守护进程的测试控制器另行跟踪。D2完整65条平台用例、新数据集总判定、D3及最终独立复核仍未完成。雷电为ARM64转译功能验证环境，真实ARM硬件和iOS保持未验证。

## 历史产物：第一轮后台修复（后续原生测试未通过）

构建来源为 `6a12a454d2f7541a57f0ad3b30c50dc4064ebb0e`，包含固定层修复 `695dee2`。六次Player/V2/Throw构建均成功，源码前后及原始Archive前后清单一致。根目录为 `E:/sgame/Builds/EntryFailureFixed`。

| 平台 | PlayerBuildId | Player目录 | V2目录 |
| --- | --- | --- | --- |
| Windows64 | b46d21fc9e494d0b8cba04cf990be9dd | Build_20261031T194903961Z_04a97628949f | Build_20261031T195306839Z_55ad1e942ff6 |
| Android | bae44f6a05884171937c0eef946bba35 | Build_20261031T195526694Z_cfc5aa88838b | Build_20261031T200002331Z_abf58615a01a |

- [x] 完整回归Edit413/413、图形Play142/142；本次D1定向70/70，结果位于 `Builds/Tests/D1/20261031T200817092Z-62e9d807`。
- [x] 两平台原始Archive完整文件、实际PE/ELF头、59/48份原元数据及7个固定编译引用逐一重算校验；V1无AddedMessage、V2有Parser；普通Player无热程序集/测试程序集AOT加载，Proof空壳无证明加载器。
- [x] Windows同一Player的V1 PID54696、V2 PID63160；Android同一APK的V1 PID23239、V2 PID23514。四次均Ready、单资源根、两个热程序集、原始metadata全部OK、零原生错误；V2新增Parser实际执行，页面显示Framework v2。四次各有 `runtime-audit.json`。
- [x] Android APK SHA256为 `2e32b95cfd0feb2e22ef31e91b4fd431993f058af0e53e062ab248b9df73aac7`，安装前验证并备份旧测试包，仅执行保留数据安装。三个原生库均AArch64，签名校验通过；V1和实际V2槽位各54文件与各自清单/签名一致。
- [x] 修复探针：Windows Throw PID81616→恢复PID78616；Android Throw PID23743→恢复PID24008。原始 `System.InvalidOperationException: D2 Delivery ThrowOnStart` 保留为 `HotfixInitializationFailed`/`Hotfix`，坏版隔离、无Ready，新的进程从v2恢复。该探针不计正式D2用例。

统一证据目录为主工程 `Builds/Tests/D2/EntryFailureFixed-20261031T194853969Z`，含构建结果、源码/Archive前后清单、`physical-compatibility.json`、两平台 `native-fix-probe.json` 及Android槽位捕获。`Android-v2-slot` 是重复捕获V1的历史脚本输出；真正V2证明为 `Android-v2-release-slot/result.json`，按V2发布清单验证，未混用。

原始运行目录均在主工程 `Builds/Tests/D1`：Windows `WindowsRuntime-d1-20261031T195354690-v1`、`WindowsRuntime-d1-20261031T195512725-v2`；Android `AndroidRuntime-d1a-20261031T200147648-v1`、`AndroidRuntime-d1a-20261031T200315760-v2`。测试拥有的原生进程和服务器均已结束。后续D2必须绑定本节新身份。

## 历史正式产物（修复前）

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
