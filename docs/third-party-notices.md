# 第三方来源与许可

## Tok 客户端信封

共享源 `../comm/proto/tok/client/v1/client.proto` 逐字节复制自本地 Tok canonical 文件 `../tok/crates/tok-proto/proto/tok/client/v1/client.proto`，保留全部消息、字段和注释，未作修改。

- 实际来源提交：`f550195d60577524e3272dda2789f07ab381d7c1`。
- 原始信封 SHA-256：`907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583`。
- Frame.version：3，来源为 `crates/tok-gateway/src/codec/mod.rs` 中的 `VERSION`。
- 网关错误码来源：`crates/tok-gateway/src/error.rs`，包含 Ok=0、AuthenticationFailed=3、Timeout=7；完整值及文件 hash 记录于共享 `protocol.lock.json`。
- 实际许可：Tok 仓库根 `LICENSE` 中的 Apache License 2.0；完整原文复制至 `../comm/proto/LICENSE-Tok.txt`，原始 SHA-256 为 `c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4`。当前来源仓库根无单独 NOTICE 文件。

上述来源锁独立于 protoc 与 protobuf runtime 工具链锁。N0 仅建立共享源契约，尚未生成或分发 C# 协议代码。

## Unity Game Framework / Game Framework

官方仓库 `https://github.com/EllanJiang/UnityGameFramework.git` 的完整源码快照置于 `Assets/ThirdParty/GameFramework`，固定提交 `e7eb3ef11393df0ac504204c480cdb839752b05f`，上游包版本 `2021.05.31`。未复制 Git 数据或旧项目代码。快照包含上游 GameFramework.dll、SharpZipLib.dll、link.xml、编辑器和运行时源码；原有 meta 和 MIT 许可 `Assets/ThirdParty/GameFramework/LICENSE.md` 保留。GF 二进制 SHA-256 记录于 `dependencies.lock.json`。

快照随带的 ICSharpCode.SharpZipLib.dll 程序集版本为 `0.86.0.518`。对应官方源码 tag `0.86.0.518` 解析到提交 `cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6`；`src/AssemblyInfo.cs` 标明 GPL v2 或更新版及独立模块链接例外。完整 GPL 原文及上游版权/例外头分别保存在 `docs/licenses/SharpZipLib-GPL-2.0.txt`、`docs/licenses/SharpZipLib-linking-exception.txt`。官方[旧版说明](https://icsharpcode.github.io/SharpZipLib/legacy.html)亦保留该例外。二进制保持 UGF 原样，hash 记录于依赖锁；不将当前新版 SharpZipLib 的 MIT 许可套用到这个旧版二进制。

## HybridCLR

官方 UPM 包以嵌入源码快照保存在 `Packages/com.code-philosophy.hybridclr`；版本 `9.0.0`、tag `v9.0.0`，固定提交 `86e6407a35d7da0b7697f055c7488ef859c51ea4`，来源 `https://github.com/focus-creative-games/hybridclr_unity.git`。保留包内 MIT 许可及 UnityHook 等随带第三方许可。InstallerController 源码 hash、HybridCLR runtime 和 il2cpp_plus 的精确提交记录于依赖锁。安装后的本地编译器位于该版本官方 `Library/HybridCLR` 路径，不进入版本管理。

包内 `Plugins/dnlib.dll` 的程序集版本为 `4.3.0.0`。对应官方来源 `https://github.com/0xd4d/dnlib.git`，tag `v4.3.0`、提交 `8f7edaf55d5e3dd8627e6776440cc7fcf6d43b28`；该 tag 的原始 MIT 许可保存在 `docs/licenses/dnlib-MIT.txt`，二进制及许可 SHA-256 记录于依赖锁。

## Unity 官方包

uGUI `1.0.0`、TextMeshPro `3.0.6`、Newtonsoft JSON `3.2.1`、Unity Test Framework `1.1.33` 以及其 NUnit `1.0.6` 依赖通过 Unity Package Manager 解析。精确版本见 `dependencies.lock.json` 和 `Packages/packages-lock.json`；各包许可和第三方通知由官方包随附，Unity 编辑器及平台工具链不随本仓库分发。

## Protobuf runtime

N1 installs Google.Protobuf `3.36.2` from its NuGet `netstandard2.0` asset and System.Runtime.CompilerServices.Unsafe package `4.5.2` (assembly `4.0.4.1`) through the shared dependency restore. Unity supplies Memory/Buffers/Numerics; those libraries are not copied into Assets. Installed DLL hashes, exact source references and license hashes are recorded in `Assets/ThirdParty/Protobuf/dependency-provenance.json`, whose hash is pinned by `docs/dependencies.lock.json`.

The protobuf source commit is `2c74169b34066ceb8ddb6b882fcb3fb32d737a55`, as recorded by the pinned NuGet package. Its BSD-3-Clause license is preserved at `Assets/ThirdParty/Protobuf/Google.Protobuf-BSD-3-Clause.txt`. Unsafe source commit `02b11eeee1fbc5f3ef43a1452fe07efd25fa1715` is recorded by that package; the original corefx MIT license is preserved at `Assets/ThirdParty/Protobuf/Unsafe-MIT.txt`. Shared restore copies these immutable license snapshots from `comm/tools/Dependencies/licenses` with the binaries. Generated protocol sources remain owned by this project's shared proto sources.

## Fixed Tok envelope

The AOT fixed envelope is generated from the canonical Tok v3 source pinned in `comm/proto/protocol.lock.json`, Tok commit `f550195d60577524e3272dda2789f07ab381d7c1`. The original Apache-2.0 license is preserved byte-for-byte in `docs/licenses/Tok-Apache-2.0.txt`; its SHA-256 is verified against the shared source lock. Generated `Client.cs` and its co-located provenance bind the exact input and generator versions.
