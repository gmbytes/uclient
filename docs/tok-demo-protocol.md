# 客户端 Proto 与 Tok demo 接入契约

日期：2026-10-28。状态：实施计划的一部分；本轮不创建协议源、生成C#或启动服务。

## 1. 协议归属

协议统一放`E:/sgame/comm/proto`，作为客户端和服务端协同维护的共享协议源。N0已根据Tok canonical定义建立兼容信封、来源锁和空正式业务准入表，测试源隔离于共享fixtures目录；客户端只保留生成后的C#及provenance，不维护另一份可编辑proto。生成和验证工具继续复用`comm/tools`。

接入Tok demo时，线格式必须兼容Tok当前实现。`comm/proto/tok/client/v1/client.proto`保存服务端完整信封的兼容副本，保留全部消息、package、字段编号、oneof和Frame版本。

服务端权威定义为`E:/sgame/tok/crates/tok-proto/proto/tok/client/v1/client.proto`。2026-10-28核对时Frame仍为v3，原始文件SHA-256为`907b6c3ceaa0e7491578c36ad46f839302a459e311248e448ed3af23daecc583`。实施时重新记录实际Tok提交和源码hash，不能沿用旧工具锁内的提交号冒充当前来源。

## 2. 目标文件

```text
E:/sgame/
  comm/proto/
    README.md
    protocol.lock.json                 Tok来源、Frame版本及原始hash
    client-messages.json               首期request/notify均为空
    tok/client/v1/client.proto          完整固定信封兼容副本
    game/README.md                     后续游戏消息边界说明，无占位业务
    tests/fixtures/                    隔离的兼容性/负例测试.proto
  client/Assets/Scripts/
    Framework/Protocol/Envelope/
      Client.Protocol.Envelope.asmdef
      Generated/Client.cs              固定信封C#，属于AOT
      source-provenance.json
    Game/Protocol/Generated/
      GameMessageRegistry.cs           首期合法空注册表
```

`comm/proto/protocol.lock.json`记录canonical相对路径、Tok提交、Frame.version=3、信封SHA、网关错误码来源文件hash和采用的关键错误码。protoc/runtime下载版本与hash仍由共享工具链锁提供；工具区分工具链锁和共享协议来源锁，必需协议缺失或空目录时明确失败。

## 3. 首期需要的消息

客户端生成完整信封类型，但首期只使用Frame.LoginRequest/LoginResponse/ControlError。接入登录不需要GetPlayer、SetPlayer、QueryGuild、业务MessageId或Notify，不因此增加游戏业务。

| 消息/字段 | 约束 |
| --- | --- |
| Frame.version | 固定3 |
| Frame.content | 登录请求使用login；回应使用logged_in或error |
| LoginRequest.request_id | 非零uint64，同连接严格递增；首次可为1 |
| LoginRequest.credential | 开发demo凭据，如`demo:42` |
| LoginRequest.resume_token | 普通凭据登录留空，与credential二选一 |
| LoginResponse.request_id | 匹配当前在途请求 |
| LoginResponse.error_code | 0成功；非零失败，不以是否收到消息判成功 |
| LoginResponse.player_id/session_id/epoch | 可信服务端返回值；成功时校验并保存 |
| LoginResponse.resume_token/TTL | 仅内存保存，首期不自动恢复/刷新，不写日志 |
| ControlError | 匹配请求并映射有限错误，未知码保留原始uint32 |

网关数字错误码来自`tok/crates/tok-gateway/src/error.rs`，当前Ok=0、AuthenticationFailed=3、Timeout=7。其他码按当前源码核对；不能猜成HTTP错误码或把不认识的非零码当成功。

## 4. TCP与本地联调

- 4字节大端正文长度前缀，长度不含前缀；正文为Protobuf Frame，不能经JSON发送。
- 先检查`0 < length <= 1,048,576`再分配；完整帧I/O期限15秒，包含等待首字节。当前没有独立心跳。
- 本地demo配置：`tok/examples/demo-game/config/local.toml`。
- 客户端TCP为`127.0.0.1:7000`；7001是内部gRPC，不能作为Unity登录端口。
- 在`E:/sgame/tok`执行`just run examples/demo-game/config/local.toml`持续运行服务；等价命令为`cargo run -p demo-game --locked -- --config examples/demo-game/config/local.toml`。
- `just client-smoke`是运行样例后退出的验证命令，不能作为常驻Unity联调服务。

Windows客户端直接连接7000。Android实机的127.0.0.1指设备自身；首期USB联调显式执行`adb -s <serial> reverse tcp:7000 tcp:7000`，之后设备可使用127.0.0.1:7000接入主机demo，验收后移除该端口映射。使用局域网地址时另配服务监听和开发地址，不把设备回环地址误认为PC。

演示流程：输入账号42→DevelopmentCredentialProvider生成demo:42→发送Frame(version3,request_id1)→收到error_code0且身份匹配的LoginResponse→显示“已登录（框架演示）”。非法凭据应显示拒绝；超时/断线如实恢复UI。登录后闲置可能因当前I/O期限断开，不用自造ping/pong绕过协议。

## 5. 生成和校验

Task N0定义`Tool/Client/Check-TokProtocol.ps1 -TokRoot E:/sgame/tok -ProtoRoot E:/sgame/comm/proto -Check`：核对完整信封原始hash、来源锁和Frame版本；任何源缺失/漂移退出非零。

Task N1让共享生成器CSharp目标显式接收`-ProtoRoot E:/sgame/comm/proto -ProtocolLock E:/sgame/comm/proto/protocol.lock.json`。固定信封与未来业务分别生成到上面的AOT/Hotfix目录；空业务准入合法，缺少固定信封不合法。schema工具只读Tok源码，协议构建唯一输入为comm/proto。

后续扩展demo业务联调时，只能从`tok/examples/demo-game/proto/demo/v1/{player,message_id}.proto`建立明确的兼容副本和准入表，并新增独立Task。ProductionFinished=11是服务端内部计时消息，不能因枚举可见就开放客户端调用；当前不实施这些业务。

## 6. 可逐项验证的通过条件

- N0：comm/proto必需文件完整；服务端canonical原始hash匹配；缺源或故意改字段/编号/来源锁即失败。
- N1：只从comm/proto生成C#；AOT信封和空热更registry分别编译，客户端无可编辑proto副本。
- N2/N3：边界帧、期限、登录成功/拒绝/取消/迟到响应通过。
- N4：C#/Tok黄金帧双向读取、真实demo:42登录7000通过；Android端口映射单独记录。
- F8/D2：生成类型在同主包IL2CPP热更场景中执行，不能仅用.NET/Editor证明。

详细实施见[Protobuf子计划](superpowers/plans/2026-10-28-csharp-hotupdate-framework-02-protobuf-session.md)。
