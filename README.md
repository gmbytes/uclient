# C# 客户端框架

Unity `2022.3.25f1 (530ae0ba3889)`，目标为 Windows x64 和 Android ARM64，均使用 IL2CPP、.NET Standard 2.1。F1 建立工程和测试基线；此阶段不证明代码或资源热更新成功。

## 本地准备

安装上述精确版本 Unity 及 Windows Build Support (IL2CPP)、Android Build Support、Android SDK/NDK Tools、OpenJDK。Windows IL2CPP 还需要 Visual Studio 的 C++ 工具链和 Windows SDK。设置 `UNITY_EDITOR_PATH` 指向 `Unity.exe`；未设置时脚本使用 `C:\Program Files\Unity 2022.3.25f1\Editor\Unity.exe`。

UGF 源码完整快照和 HybridCLR 嵌入 UPM 包已入库，官方 Unity 包由 UPM 按 `Packages/packages-lock.json` 解析；来源、版本及二进制 hash 见 `docs/dependencies.lock.json`。不需要旧客户端 Assets，也不安装 Addressables、Lua 或 Protobuf（Protobuf 归 N1）。

首次使用 HybridCLR 时，按依赖锁的 runtime 和 il2cpp_plus 精确提交准备源码：以 il2cpp_plus 的 `libil2cpp` 目录为根，将 runtime 仓库的 `hybridclr` 子目录放入该根目录。调用编辑器方法 `Client.Editor.Build.ProjectSetup.InstallHybridCLR` 并传入 `-hybridclrSource <libil2cpp目录>`，例如：

```powershell
& $env:UNITY_EDITOR_PATH -batchmode -nographics -quit -projectPath $PWD -executeMethod Client.Editor.Build.ProjectSetup.InstallHybridCLR -hybridclrSource E:/local/assembled-libil2cpp -logFile Builds/install-hybridclr.log
```

此方法调用官方 `InstallerController.InstallFromLocal`，安装结果位于忽略的 `Library/HybridCLR`。不要用 Mono 替代平台 IL2CPP 工具链。`ProjectSetup.CreateBootstrap` 是资产制作工具，会重建场景和 prefab；普通 checkout、测试和构建不需要执行它。

## 验证入口

从工程根目录使用 PowerShell 7：

```powershell
./Tool/Client/Test-Client.ps1 -Task F1 -Mode EditMode
./Tool/Client/Test-Client.ps1 -Task F1 -Mode PlayMode
./Tool/Client/Smoke-Player.ps1 -Platform Windows64
./Tool/Client/Smoke-Player.ps1 -Platform Android
./Tool/Client/Tests/Test-TokProtocol.ps1
```

测试按 NUnit Category 选择 Task，零测试、跳过或失败均不会输出 `TASK_PASS`。测试 XML、完整 Editor 日志、命令参数和退出码保存在 `Builds/Tests/<Task>/<runId>`。每次平台构建使用新目录 `Builds/Player/<Platform>/<runId>/Player`，记录 Editor 日志和 JSON 构建结果；成功必须退出 0、有平台产物且 backend 为 IL2CPP。

构建结果的 `totalBuildBytes` 是 Unity BuildReport.summary.totalSize；`artifactBytes` 才是输出 APK/EXE 文件的实际长度。Windows EXE 长度不代表整个 Player 目录大小。早期 F1 归档中的 `bytes` 字段只表示前者，原始证据保持不变。

Android 工具安装在 Unity 默认模块目录以外时，可设置 `ANDROID_SDK_ROOT`、`ANDROID_NDK_ROOT`、`JAVA_HOME`。构建入口会使用这些已有目录，并把实际 SDK/NDK/JDK 路径记录在 `build-result.json`；这些机器路径不写入仓库的 ProjectSettings。

仓库清单查询受网络限制时，官方 SDK manager 支持 `SDK_TEST_BASE_URL` 指向经校验的原始 repository XML 基址（URL 以 `/` 结尾，也支持 `file:///`）。该环境变量会由构建进程继承，并与工具链环境一起记录在每次构建的 `result.json`；SDK 工具、原生编译和 APK 生成仍照常执行。

Bootstrap 只含一个 GameFramework prefab 根节点，带 Base、Event、Download。Resource 和 UI 留待后续任务初始化；HybridCLR 暂无热更程序集配置，因此 F1 构建会保留官方插件的对应提示。
