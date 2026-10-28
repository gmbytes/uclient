using Client.Contracts;
using Client.Framework.Network;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Client.Tests.Managed;

[TestClass]
public sealed class RealTokDemoTests
{
    [TestMethod]
    [TestCategory("RealTok")]
    public async Task Account42LogsInAndDisconnectsAgainstRealTok7000()
    {
        RequireRealServer();
        using var session=new SessionClient(new TcpFrameConnection(),new SessionOptions("127.0.0.1",7000), context:new InlineContext());
        var input=Client.Game.Login.DevelopmentCredentialProvider.Create("42");
        Assert.AreEqual("demo:42",input.Credential);
        var result=await session.LoginAsync(input,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.IsTrue(result.Succeeded,$"{result.Error}/{result.ServerErrorCode}"); Assert.AreEqual(0U,result.ServerErrorCode);
        Assert.AreEqual("42",result.Identity.PlayerId); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Identity.SessionId));
        Assert.IsTrue(result.Identity.Epoch>0); Assert.AreEqual(SessionState.Authenticated,session.State);
        session.Disconnect(); Assert.AreEqual(SessionState.Disconnected,session.State); Assert.IsNull(session.Identity);
    }
    [TestMethod]
    [TestCategory("RealTok")]
    public async Task InvalidCredentialIsRejectedByRealTok()
    {
        RequireRealServer();
        using var session=new SessionClient(new TcpFrameConnection(),new SessionOptions("127.0.0.1",7000), context:new InlineContext());
        var result=await session.LoginAsync(new LoginInput("invalid-n4-credential",null),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.IsFalse(result.Succeeded);Assert.AreEqual(LoginError.Rejected,result.Error);Assert.AreEqual(3U,result.ServerErrorCode);
        Assert.AreEqual(SessionState.Disconnected,session.State);Assert.IsNull(session.Identity);
    }
    [TestMethod]
    [TestCategory("RealTok")]
    public async Task AuthenticatedIdleConnectionObservesRealGatewayDisconnect()
    {
        RequireRealServer();
        // Longer local read deadline ensures the default gateway's 15s idle close is observed.
        var transport=new TcpFrameTransport(new TransportOptions(readFrameTimeout:TimeSpan.FromSeconds(30)));
        using var session=new SessionClient(new TcpFrameConnection(transport),new SessionOptions("127.0.0.1",7000), context:new InlineContext());
        var disconnected=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged+=state=>{if(state==SessionState.Disconnected)disconnected.TrySetResult(true);};
        var result=await session.LoginAsync(Client.Game.Login.DevelopmentCredentialProvider.Create("42"),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.IsTrue(result.Succeeded);await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(22));Assert.IsNull(session.Identity);
    }
    static void RequireRealServer()=>Assert.AreEqual("1",Environment.GetEnvironmentVariable("SGAME_REAL_TOK"),"RealTok tests require an explicitly owned running demo server; never substitute fixtures.");
    sealed class InlineContext:SynchronizationContext { public override void Post(SendOrPostCallback callback,object state)=>callback(state); }
}
