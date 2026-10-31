# 客户端框架实施进度

开始日期：2026-10-29。目标为完整实现[总计划](superpowers/plans/2026-10-28-csharp-hotupdate-framework.md)并完成相应检测；当前未完成整个目标。

## 当前状态

当前正式完成 **18/21 Task**。原生后台恢复实测暴露输入焦点/选区丢失，框架修复已完成26/26定向回归及413/148完整回归；正在验证原生第二帧键盘撤销修复；D1需生成修后新Player重新复验，D2/D3仍未完成。已完成项同时在子计划中勾选步骤，本表保留提交与检测证据。

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
| F8 | 已验证并通过独立审查 | bc4c924，主分支整合0833260；新Windows/雷电ARM64 APK各五场景、343/343 Edit、135/135图形Play、34项判定器及原始证据重评通过；真实ARM/iOS未测 |
| N1 | 已验证并通过独立审查 | 3a894568/75516cd；生成器54/54、Unity22/22+4/4，实际Protocol→Game同Player证明 |
| N2 | 已验证并通过独立审查 | 8756a77/822b166；23/23+13/13，重连代次与超时发布竞态修复 |
| N3 | 已验证并通过独立审查 | 56c3d78/4d24c95；并发邮箱修复及主工程241/241+69/69通过 |
| N4 | 已验证并通过独立审查 | ccd714c/6d8e0f0、comm375d1dc/60ac62e；Rust互验、实际Tok3项及边界修复通过 |
| U1 | 已验证并通过独立审查 | 19a492a；统一屏幕快照，合并回归154/154+39/39通过 |
| U2 | 已验证并通过独立审查 | 6c85afb/f24163e；40组合矩阵、键盘手动滚动及恢复，合并回归通过 |
| U3 | 已验证并通过独立审查 | 27a0b7f/430a170；合规层级16+10项及合并回归通过 |
| U4 | 已验证并通过独立审查 | 8212d63/43093f8；工具/预览/满屏背景与局部安全区，合并回归通过 |
| U5 | 已验证并通过独立审查 | c752806/b55dbc1/639f447；完整310/95，主工程9项Edit及16项真实图形集成通过 |
| D1 | 后台焦点修复后待新基线复验 | UIFocusLifecycle补齐同控件焦点/双向选区恢复及关闭/切页隔离；原生第二帧撤销复现RED后26/26 U2通过；413/148完整回归通过，修后原生验证待完成；此前b46/bae证据保留原始身份 |
| D2 | 原基线Android24/31、Windows13/34保留 | 当前b46/bae原包验证到13/24项后发现后台编辑焦点问题，未通过用例未计完成；修后基线需重新建立正式数据集。工具330项通过，GF语义坏表/回退/磁盘新证据保留 |
| D3 | 文档准备 | 已提供运行时架构、迁移台账与iOS后续清单；干净checkout复现和最终交付审计未完成 |

## 2026-11-01 D1完成检查点

正式Windows更新端点使用localhost，解决当前代理环境对127.*例外的行为差异；没有修改全局代理。隔离诊断副本不计验收，随后重新构建了正式Player a019389c3f5342b1be60c06c1c2090a5及独立V2/Throw补丁。正式V1 PID79604、V2 PID41008均Ready且零未预期原生异常/启动失败，真实下载成功、新Parser执行、Framework v2资源展示、七固定引用和59份原元数据不变，源码/原归档恢复与原生包前后hash一致。Android当前31a7基线及48份元数据/54文件同包证据保持有效。最终D1字面命令为70/70、零跳过、退出0且源码hash一致。总计划、子计划D1五步及本表同步标记完成；完整证据和环境限制见[D1验收记录](verification/d1-build-acceptance.md)。D2/D3和M4仍未完成。

## 2026-10-31 修复与检测历史

当前候选主包与补丁已从`7665bf9`在固定工程路径生成，四次构建均Succeeded、源文件恢复一致、原始Archive前后逐文件一致。Windows PlayerBuildId为`d967517226c743b3802903992546d632`，Android为`31a7aaaf1c0f45a49d82eed7eb0047da`。Android修后同APK V1 PID1982、V2 PID2228均Ready、零原生异常、48份元数据全部OK、单资源根/两个热程序集；V2实际执行新增AddedMessage Parser并展示Framework v2。两版物理slot各54文件及原始清单/签名全部匹配，安装APK SHA256始终为`97121e793f56041f0086ec877728b9ef03a8c105af46d4571d601eead1f6d758`。原始证据在`Builds/Tests/D1/CanonicalFinal-20261031T152623672Z`、`AndroidRuntime-d1a-20261031T154442369-v1`、`AndroidRuntime-d1a-20261031T154609102-v2`及`CallbackAndroidV1Slot-20261031`、`CallbackAndroidV2Slot-20261031`。APK保留数据安装一次，旧包已验证归属并备份；测试拥有的进程、服务与18765映射已释放。Android仍为雷电ARM64/Houdini功能验收，真实ARM/iOS未测。

Windows修后V1已Ready且零原生异常，但在线检查被系统代理返回502；原始PreparationFailed记录保留，不能据此算在线更新通过。D1 Step3/4构建流程已完成，Step5和整个D1保持未完成。新增参考图登录页、多屏美术改造单独跟踪，当前优先继续原21项框架Goal。

最终候选源码`9a96dc1`的Windows和雷电Android同主包V1/V2均达到Ready，执行新增Parser和Framework v2；Windows59份、Android48份元数据加载OK。Android V1/V2各54个落盘文件与签名清单一致，APK未变。实际Editor菜单Patch成功且源码恢复通过；这些事实保留为阶段性证据。

完成审计进一步发现启动期间GF集合遍历异常，以及一次退出取消订阅异常，因此D1未标完成。`ac44f88`将下载完成续体排回Unity主线程，并使清理兼容GF事件池已关闭的销毁顺序。三个新增行为用例均先失败再通过，定向6/6；完整Edit413/413、图形Play140/140，均零失败/跳过、进程退出0、源码前后hash一致。原始证据为`Builds/Tests/Integration/D1/20261031T151510924Z-PlayMode`（RED）、`20261031T151835282Z-PlayMode`（RED）、`20261031T151933345Z-PlayMode`（GREEN）及`20261031T152026345Z-EditMode`、`20261031T152230628Z-PlayMode`（全量）。必要的代码注释已提交；修后两平台原生基线与同包热更须重新验证。

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

这些结果证明Editor中的启动事务、真实UI帧与集成行为，不代替最终Release IL2CPP主包。F8已在新Windows与雷电Android ARM64基线上完成同APK/EXE的v1→v2代码及资源更新、真实ThrowOnStart下进程回退和更新端点不可达启动，并通过独立审查与源码哈希整合核对。D1–D3最终归档、完整设备/故障矩阵和干净checkout交付尚未完成。通过日志中的Unity授权/SSL环境诊断仍保留为已知限制，不称为无噪声日志。

## F8 双平台核心闭环

当前完成18/21个Task，M1热更核心门槛通过。Windows run为20261029T005757118Z-8f8bb21a，Android run为20261029T010559687Z-48bad4d2；两平台均保留同主包、五个独立进程、原始归档/签名/元数据和实际UI证据。Android为用户选择的雷电ARM64/Houdini环境，不代表真实ARM硬件验证。完整操作与限制见[热更新验证](verification/hotupdate.md)。

最后判定器修复明确要求实际ThrowOnStart标记、绑定本次release/epoch的预期异常，且拒绝成功entry-return。10项新负例RED后，34项GREEN；两平台原始记录重评通过，2805个原生证据文件保持不变。全量Edit343/343与图形Play135/135的Unity输入未变。主分支保留后续六份文档，仅两个PS文件承载已审查修复，其余源码与冻结测试输入逐项一致。D1正式主包和补丁、D2完整矩阵、D3干净checkout仍须继续。

## D1 当前检查点

D1 的实现与证据目前位于独立工作区 `E:/sgame/.worktrees/client-screen-layout`，尚未作为完整交付整合到主工程。统一 Run/Execute、Player/Patch/资源包编排、原生公开数据配置与验收观察点已实现并导入；当前修后AOT检查点 `28fd6f7` 已通过窄范围复审。Windows及雷电Android核心链路已实测；Editor成功菜单入口、最终构建文档与完整D1整合审查仍待完成。

出包前审查发现私钥路径可能进入产物、同一验收用例重启时证据文件冲突，以及校验器错误限制外部输出目录三项问题。均先保留真实 RED，再修复验证：证据隔离11/11、私钥位置10/10、外部输出5/5；`Builds/Tests/D1/Fix2-Green-20261029T172431923Z` 为54/54、零失败/跳过、Unity退出0，源码前后哈希一致。限定复审已通过；这些Editor/PowerShell结果不代替最终原生主包验证。

在冻结的 `7ba8acd` 输入上，全量EditMode为397/397、非batch图形PlayMode为135/135，均零失败/跳过、正常退出且纳入快照的源码前后哈希一致。自动生成的默认设置已核对、留证并清理。之后的窗口根GameObject身份修正有真实对象1/1回归和窄审批准；Editor构建来源归档补充有2/2定向验证及审查批准。397/135对应其原始输入，后续两项各自保留独立来源与结果。锁定依赖恢复使用共享脚本显式指定ClientRoot，protoc36.2及ZIP/hash已核对，源依赖字节保持不变。

首次Windows真实CLI构建的IL2CPP主包本身成功，但整次事务因空场景恢复报错而Failed，未发布可用渠道；原始证据 `Builds/Tests/D1/Native-Player-Windows64-20261029T190239024Z` 保留。Editor恢复修正 `c50face` 经过真实Execute的0/1→1/1回归及窄审，通过后重新构建。

第二次Windows CLI构建完整Succeeded，实际输出为 `E:/sgame/Builds/D1Final/Build_20261029T191420650Z_f20e1fa0dc9e`，PlayerBuildId为 `8836fa139c884865a1e510f11f647f5f`；原始归档、严格兼容检查和源设置恢复通过。但该Player首次运行在版本选择前的身份检查失败，未到Ready。这些旧产物保持原始身份，不作为修后AOT的运行结果。

实际失败推动两项AOT修正：Win32独立程序按真实平台、Unity版本和目标位宽核对原生身份，记录实际applicationId和独立profilePackageId，Android仍严格核对OS包名；输入观测改为支持当前UILogin的真实uGUI InputField。修后 `28fd6f7` 的23/23定向回归及窄审通过，原签名、完整Player身份、元数据和七引用门禁保持。当时Android仅完成设备级重连与目标包未安装预检；后续真实构建和运行结果见下。

修后Windows原始PlayerBuildId为 `02014a45a75b4bfd8befc56ff30ddbd1`，输出 `E:/sgame/Builds/D1Final/Build_20261029T194327321Z_980e5b83b8ff`。其V1实际Ready；独立V2 Patch显式使用该原归档，输出 `Build_20261029T194927754Z_2cd84002d268`，7个固定引用逐项相等，59份原始metadata与Patch实际文件SHA全部相等。V1无AddedMessage/Parser，V2包含并实际执行集合、map、未知字段、uint64、热泛型及空Registry探针，随后Ready。两运行的原生文件清单保持一致。

Windows实际记录在 `Builds/Tests/D1/WindowsRuntime-d1-20261029T194739006-v1` 与 `WindowsRuntime-d1-20261029T195041888-v2`：PID97160/6036均退出0，59条metadata加载记录各全部OK；物理client720×1280、可调整窗口、DPI144，观察线程使用PER_MONITOR_AWARE_V2。实际720×1280截图中的CJK、本地模拟与Framework v1/v2标记可读。这些是Windows里程碑，不代表D2完整故障、强杀、UI/IME或真实Tok综合矩阵已经通过。

新增测试proto已迁到共享comm提交 `93faaa1` 的 `proto/tests/fixtures/delivery`。客户端只保留生成C#与来源记录；缺源/变源负例拒绝、重新生成字节一致和补充编译已核实。用户原有README删除保持未暂存。整体仍为18/21个Task完成，D1、D2、D3继续推进。
## 2026-10-31 Android D1 原生里程碑

冻结源码仍为 `28fd6f727b71e8fe23fb5fc4f0abafd151c2ce02`。Android CLI Player与独立V2 Patch均实际退出0，源码恢复检查通过。主包输出 `E:/sgame/Builds/D1Final/Build_20261031T060414131Z_392b04008e61`，PlayerBuildId为 `1368af463434440cb6f1db4e41c9db5e`；补丁输出 `Build_20261031T060936043Z_49c42661837e`，显式使用原始Archive。7个固定编译引用及48份原始AOT元数据已逐文件重算SHA，与补丁完全相等。

雷电实例0、Android14、设备 `emulator-5554` 上首次安装该测试包，安装前包不存在。实际APK只有ARM64原生库，ELF机器头均为AArch64；包名、INTERNET权限与签名校验通过。同一APK上V1 PID2173、V2 PID2408均实际Ready，48条metadata记录各全部OK、一个资源根、两个热程序集、无启动失败。V2实际执行 `AddedMessage list,map,unknown-fields,uint64,hot-generics,empty-registry` 并显示 `Framework v2`；1440×2560原始截图中的CJK登录界面可读。

原始运行记录位于 `Builds/Tests/D1/AndroidRuntime-d1a-20261031T061700247-v1` 和 `AndroidRuntime-d1a-20261031T061753610-v2`。两个实际PID的ActivityManager记录均为 `mRequiredAbi=arm64-v8a instructionSet=arm64`，包路径与安装APK对应；设备主ABI为x86_64、native bridge为libhoudini.so。安装后实际APK SHA为 `91fbc820a3550fe6f8aded9b68d7c1de16801265c3a260ab335ad071408eec65`，与原产物一致，安装次数为1。真实ARM硬件与iOS仍未测。

首次观察脚本因受限的 `/proc/PID/exe` 读取提前停止PID2078；该次没有产生原生启动事件，不计通过，原始失败记录保留。后续保留exe/maps权限失败，采用同PID系统进程记录核对ABI，没有修改应用或放宽Ready条件。V1/V2测试进程均在核对PID与内核启动时间后停止；拥有的回环服务器正常退出，仅移除本次ADB反向映射。以上属于D1里程碑，不能替代D2完整设备矩阵。

## D2 检测工具检查点

独立工作区 `client-release-verifier` 已提交准备检查点 `40757ddf3fd36b3a6cec0a7c1fbb80727baaa400`。本次完整运行7个工具套件：153判定器、15文件/机器头、11采集器、12进程所有权/执行门禁、11部分传输、4Throw fixture来源及34项原F8判定，共240项通过；14个修改脚本语法检查通过。证据位于 `Builds/Tests/D2/Resume-20261031T060220056Z-51f32ad7`。这些是工具测试，所有D2实际用例仍为NotRun，原生执行门禁未移除。整体仍为18/21，D1–D3未宣称完成。
