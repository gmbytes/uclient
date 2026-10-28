# 第三方来源与许可

## Tok 客户端信封

共享源 `../comm/proto/tok/client/v1/client.proto` 逐字节复制自本地 Tok canonical 文件 `../tok/crates/tok-proto/proto/tok/client/v1/client.proto`，保留全部消息、字段和注释，未作修改。

- 实际来源提交：`f550195d60577524e3272dda2789f07ab381d7c1`。
- 原始信封 SHA-256：`907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583`。
- Frame.version：3，来源为 `crates/tok-gateway/src/codec/mod.rs` 中的 `VERSION`。
- 网关错误码来源：`crates/tok-gateway/src/error.rs`，包含 Ok=0、AuthenticationFailed=3、Timeout=7；完整值及文件 hash 记录于共享 `protocol.lock.json`。
- 实际许可：Tok 仓库根 `LICENSE` 中的 Apache License 2.0；完整原文复制至 `../comm/proto/LICENSE-Tok.txt`，原始 SHA-256 为 `c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4`。当前来源仓库根无单独 NOTICE 文件。

上述来源锁独立于 protoc 与 protobuf runtime 工具链锁。N0 仅建立共享源契约，尚未生成或分发 C# 协议代码。
