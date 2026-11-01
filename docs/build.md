# 主包与补丁构建

> 当前状态：首期框架21/21 Task已完成并通过最终独立审查。Windows24/24、雷电Android28/28正式原生验收通过，工具回归382/382；当前PlayerBuildId与证据见[D1产物](verification/d1-build-acceptance.md)和[最终交付核对](verification/delivery-audit.md)。iOS、真实ARM硬件/性能、生产接入及登录美术改造仍属后续。

统一构建入口与实际Editor菜单已有D1证据；修后固定目录原生D1验收已通过。完整D2矩阵与D3干净checkout复现仍需各自验收。

## 准备输入

使用Unity2022.3.25f1、PowerShell7、.NET SDK和锁定的HybridCLR本地工具链。Windows需要IL2CPP/C++工具链；Android使用ARM64、NDKr23b、JDK11和SDK32。UGF/HybridCLR来源及库hash见[依赖锁](dependencies.lock.json)。

`comm`必须与client并列，包含`proto`和共享工具。首先从client根执行锁定依赖恢复，显式绑定当前工程，不能从另一个工作区默默借用protoc缓存：

```powershell
$env:SGAME_COMM_ROOT = 'E:/sgame/comm'
& "$env:SGAME_COMM_ROOT/tools/Dependencies/Restore-Dependencies.ps1" -ClientRoot $PWD
```

HybridCLR本地源码按README的精确提交准备。选择顺序是`-HybridClrSource`参数、`SGAME_HYBRIDCLR_SOURCE`环境变量、当前工程的`Builds/dependencies/assembled-libil2cpp`。共享协议默认在当前client相邻的comm，隔离工作区应显式设置`SGAME_COMM_ROOT`。不能默默借用另一个client的Library或工具缓存。

Android工具默认来自所选Unity安装的`Data/PlaybackEngines/AndroidPlayer/{SDK,NDK,OpenJDK}`。外置工具使用`ANDROID_SDK_ROOT`、`ANDROID_NDK_ROOT`、`JAVA_HOME`覆盖，构建入口保留调用者的值。`SDK_TEST_BASE_URL`只在调用者显式设置时继承，不自动选择其他工程的SDK仓库缓存。例如本机验收使用：

```powershell
$env:ANDROID_SDK_ROOT = 'E:/sgame/.tools/unity-android-2022.3.25f1/sdk'
$env:ANDROID_NDK_ROOT = 'E:/sgame/.tools/unity-android-2022.3.25f1/ndk/android-ndk-r23b'
$env:JAVA_HOME = 'E:/sgame/.tools/unity-android-2022.3.25f1/jdk11'
# 仅在已有经过校验的官方仓库XML镜像时设置：
# $env:SDK_TEST_BASE_URL = 'file:///E:/verified-sdk-metadata/'
```

关闭同工程的其他Unity进程；菜单构建使用已导入稳定的Editor。保存或关闭脏场景。输出必须为新目录，不能指向Assets/Packages/ProjectSettings/Tool/Tests/docs或原始归档内部。

## 固定编译目录

每个Player与其后续Patch使用同一个固定绝对工程目录。本项目当前固定为`E:/sgame/client`。Unity编译产物包含源文件路径，迁移工程目录可能改变固定DLL的完整hash；这种情况应建立新Player归档。干净checkout应先在自己的固定目录生成Player，再从该目录制作补丁。

## 独立公开配置与签名密钥

Player需要`-PublicProfile`，每次构建需要`-SigningKey`。公钥配置是本地可信构建输入，包含schemaVersion、keyId、RSA-2048 modulus/exponent、固定更新endpoint、acceptance、packageId、sessionHost/sessionPort。私钥必须与所选公钥匹配；不能由下载清单或私钥自动建立信任。

开发用配置示例结构如下，modulus/exponent需使用实际公钥的Base64值：

```json
{
  "schemaVersion": 1,
  "keyId": "local-demo",
  "modulus": "<RSA-2048 public modulus in Base64>",
  "exponent": "AQAB",
  "endpoint": "http://127.0.0.1:18765/",
  "acceptance": false,
  "packageId": "com.sgame.client.demo",
  "sessionHost": "127.0.0.1",
  "sessionPort": 7000
}
```

普通运行使用acceptance=false。受控验收配置才能开启原生观察/暂停选项；它不提供任意状态写入、验签绕过或信任修改接口。此字段与商店签名、生产认证无关。

私钥文件放在独立、忽略的目录，例如`Builds/PrivateKeys/LocalDemo/signer.xml`；公开配置可放`Builds/Profiles/local-demo.json`。不要放入Assets、归档、资源包或输出目录，不要提交私钥。配置和密钥需要用户本地准备，文档占位值不能直接构建。

## CLI：创建新Player

从client根使用统一入口。这里使用已准备的本地配置和本地HybridCLR源码目录：

```powershell
$profile = (Resolve-Path 'Builds/Profiles/local-demo.json').Path
$signer = (Resolve-Path 'Builds/PrivateKeys/LocalDemo/signer.xml').Path
$hybridClr = 'E:/local/assembled-libil2cpp'
$player = ./Tool/Client/Build-Client.ps1 -Kind Player -Platform Windows64 `
  -OutputRoot 'E:/sgame/Builds/LocalDemo' -PublicProfile $profile `
  -SigningKey $signer -HybridClrSource $hybridClr -ReleaseId v1
$player.OutputDirectory
$player.ResultFile
```

Android将Platform改为Android。两平台分别生成新PlayerBuildId和归档，不能混用。默认Release IL2CPP；Windows初始客户区720×1280且可调整，Android先竖屏，运行后允许横屏与竖屏切换。实际APK包含INTERNET权限。

Player流程执行共享协议生成、目标平台热程序集编译、HybridCLR有界固定点、完整GFPackage、包内基线、原生构建、最终post-strip一致性与原始归档。构建日志位于`Builds/BuildInvocations/<run>/`；`request.json`、`toolchain.json`、`process.json`、`receipt.json`和`invocation-result.json`关联本次实际输入、工具路径与结果。

使用返回的OutputDirectory/ResultFile，不能从OutputRoot下的最新文件夹推断结果。每次输出为新的`Build_<UTC>_<随机ID>`。成功要求实际进程正常退出、BuildResult成功、归档完成、恢复检查通过；存在Player/APK或pending文件本身不等于整次事务成功。

## CLI：从原归档创建补丁

保留原Player输出及Archive原始字节，在另一次调用中显式选择它：

```powershell
$archive = Join-Path $player.OutputDirectory 'Archive'
$patch = ./Tool/Client/Build-Client.ps1 -Kind Patch -Platform Windows64 `
  -OutputRoot 'E:/sgame/Builds/LocalDemo' -PlayerArchive $archive `
  -PublicProfile $profile -SigningKey $signer `
  -HybridClrSource $hybridClr -ReleaseId v2
$patch.OutputDirectory
$patch.ResultFile
```

Patch恢复原生公开配置/编译输入，执行官方AOT兼容检查，要求七个固定引用完整SHA一致，复用该Player的原始元数据。它只构建热DLL和完整资源快照，不运行GenerateAll替换原归档元数据，不修改APK/EXE。缺AOT API、固定引用或身份不匹配时要求新Player。

每个releaseId对应不可变完整快照；修订内容使用新ID。资源根在进程中只能初始化一次，热程序集不能原地卸载；新补丁在完整重启后的新进程执行。

D1验收使用隔离fixture覆盖层，普通构建不需要它：V1为`-HotfixOverlay Tests/Fixtures/Delivery/V1`，V2为`.../V2`。可编辑proto只在`../comm/proto/tests/fixtures/delivery`；客户端fixture保留生成C#与来源记录，正式业务准入仍为空。

## Editor菜单

先执行`./Tool/Client/Build-LinkerPolicy.ps1`，确认Editor当前目标平台与请求Platform一致。准备JSON请求，字段对应BuildRequest：

```json
{
  "Kind": "Patch",
  "Platform": "Android",
  "OutputRoot": "E:/sgame/Builds/LocalDemo",
  "PlayerArchive": "<original successful Player output>/Archive",
  "PublicProfilePath": "<absolute public profile path>",
  "SigningKeyPath": "<absolute private signer path>",
  "LinkerPolicyPath": "<client>/Builds/LinkerPolicy/policy.json",
  "ReleaseId": "v2-menu",
  "HotfixOverlay": "<client>/Tests/Fixtures/Delivery/V2"
}
```

通过`Game > Build > Select Request`选择文件，再用`Game > Build > Execute Selected Request`执行。菜单和CLI均调用ClientBuild.Execute，Console输出`CLIENT_BUILD_RESULT <实际ResultFile>`。查看该文件的Status/Errors/EvidenceFiles；失败不应留下可用channel指针。

构建拥有项目互斥与source/settings/config的finally恢复。不要强杀其他Unity进程。私钥位置和输出路径预检在实际构建前拒绝非法输入。

## 本地验证更新

在独立控制目录写入`active-release.txt`，值为相对ReleaseRoot的`<实际Build子目录>/Release`。只提供该Release目录，不提供私钥目录：

```powershell
./Tool/Client/Serve-TestReleases.ps1 -ReleaseRoot 'E:/sgame/Builds/LocalDemo' `
  -ControlRoot 'E:/sgame/Builds/LocalDemoServer' -ListenAddress 127.0.0.1 -Port 18765
```

端点仅允许显式loopback地址。Android使用所选序列号创建拥有的`adb reverse tcp:18765 tcp:18765`，完成后仅移除此映射。服务器通过控制目录中新建`server-stop`退出。发布到外部服务不属于这些命令的行为。

运行主包先确认V1登录页，再将测试端点指向独立V2输出，完整重启同一个Player。验收应关联实际PID、PlayerBuildId、元数据、Parser标记、UI资源版本与主包前后hash，不能仅看下载成功。

坏版下进程回退与渠道回滚的区别见[热更新发布说明](hotupdate-release.md)。完整故障/UI矩阵由D2单独验收。

## 已保留的阶段性产物

当前D1已通过验收。正式Windows PlayerBuildId为`a019389c3f5342b1be60c06c1c2090a5`，Player为`E:/sgame/Builds/D1WindowsLocalhost/Build_20261031T171435889Z_defa7e9a89b6`，V2为同根下`Build_20261031T171843848Z_757a3924da59`；Android继续使用下面31a7基线。两平台正式同包V1/V2均已实际Ready、零未预期原生异常、原包未变。完整逐项证据见[D1验收记录](verification/d1-build-acceptance.md)。

Windows公开原生配置为`http://localhost:18765/`，Android为`http://127.0.0.1:18765/`。本机服务仍显式监听127.0.0.1；localhost已经实际请求验证。前者解决当前系统代理例外下的IP端点502，未修改全局代理设置。原生配置变化使用新Player，不能通过修改旧包数据或运行参数替代正式构建。

此前修复GF回调与退出顺序的阶段性产物来自`7665bf9`，根目录为`E:/sgame/Builds/D1Final`：

| 平台 | PlayerBuildId | 主包输出 | V2补丁输出 |
| --- | --- | --- | --- |
| Windows64 | d967517226c743b3802903992546d632 | Build_20261031T152638077Z_3bb638b9dc9b | Build_20261031T153446532Z_71ae39cceab1 |
| Android | 31a7aaaf1c0f45a49d82eed7eb0047da | Build_20261031T153023251Z_38d2234aecce | Build_20261031T153558742Z_93c876f70c40 |

四次构建成功且源码恢复一致。Android同APK V1/V2完成原元数据与完整slot校验；该旧Windows IP端点产物保留其502失败记录，正式验收使用新的a019基线。以下较早产物仅保留历史证据，不替代当前源码验收：

| 平台 | PlayerBuildId | 主包输出 | V2补丁输出 |
| --- | --- | --- | --- |
| Windows64 | 02014a45a75b4bfd8befc56ff30ddbd1 | Build_20261029T194327321Z_980e5b83b8ff | Build_20261029T194927754Z_2cd84002d268 |
| Android | 1368af463434440cb6f1db4e41c9db5e | Build_20261031T060414131Z_392b04008e61 | Build_20261031T060936043Z_49c42661837e |

这些产物根目录为`E:/sgame/Builds/D1Final`，使用独立D1测试公钥配置。两平台实际V1/V2已Ready；Android为雷电ARM64/Houdini环境。实际菜单Patch已成功输出`Build_20261031T091432944Z_71c99518c60b`（releaseId=v2-menu），Editor正常退出且sourceUnchanged。D2完整矩阵、D3干净checkout和iOS仍不能据此勾选完成。
