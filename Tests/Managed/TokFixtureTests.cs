using Client.Framework.Network;
using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tok.Client.V1;

namespace Client.Tests.Managed;

[TestClass]
public sealed class TokFixtureTests
{
    const ulong Request = 9007199254740993UL;
    static readonly ulong[] Values = { 9223372036854775808UL, 9223372036854775809UL, ulong.MaxValue, Request };
    static ByteString Binary => ByteString.CopyFrom(new byte[] { 0, 128, 255 });
    static string Vectors => Environment.GetEnvironmentVariable("SGAME_TOK_INTEROP_ROOT") ??
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Tests/Fixtures/ProtocolInterop/Vectors"));

    [TestMethod]
    public void AllRustFrameOneofsMatchEverySemanticFieldAndCSharpEmitsOwnFrames()
    {
        var frames = new[] {
            new Frame { Version=3, Login=new LoginRequest { RequestId=Request, Credential="fixture-only" } },
            new Frame { Version=3, LoggedIn=new LoginResponse { RequestId=Request, PlayerId="p", SessionId="s", Epoch=ulong.MaxValue, ResumeToken="fixture-token", ResumeTokenTtlMs=Request } },
            new Frame { Version=3, Request=new BusinessRequest { RequestId=Request, MessageId=1, Body=Binary } },
            new Frame { Version=3, Response=new BusinessResponse { RequestId=Request, MessageId=2, ErrorCode=14, Body=Binary, ServerSeq=ulong.MaxValue } },
            new Frame { Version=3, Notify=new Notify { MessageId=3, Body=Binary, ServerSeq=ulong.MaxValue } },
            new Frame { Version=3, Error=new ControlError { RequestId=Request, ErrorCode=14 } },
            new Frame { Version=3, Ack=new Ack { AckSeq=ulong.MaxValue } },
            new Frame { Version=3, Resume=new ResumeRequest { RequestId=Request, ResumeToken="fixture-token", AckSeq=ulong.MaxValue } },
            new Frame { Version=3, Resumed=new ResumeResponse { RequestId=Request, PlayerId="p", SessionId="s", Epoch=ulong.MaxValue, LastRequestId=Request } },
            new Frame { Version=3, Recovered=new RecoveryComplete { ThroughSeq=ulong.MaxValue } },
            new Frame { Version=3, RefreshResumeToken=new RefreshResumeTokenRequest { RequestId=Request } },
            new Frame { Version=3, ResumeTokenRefreshed=new RefreshResumeTokenResponse { RequestId=Request, ResumeToken="fixture-token", ResumeTokenTtlMs=Request } }
        };
        var output=Path.Combine(Vectors,"CSharp"); Directory.CreateDirectory(output);
        for (int i=0;i<frames.Length;i++)
        {
            var wire=File.ReadAllBytes(Path.Combine(Vectors,"Rust",$"frame-{i:00}.bytes"));
            var actual=FrameCodec.Decode(wire);
            Assert.AreEqual(3U,actual.Version,$"Frame {i} version");
            Assert.AreEqual((Frame.ContentOneofCase)(i+2),actual.ContentCase,$"Frame {i} oneof");
            // Generated equality visits all scalar/bytes/oneof fields; wire byte order is irrelevant.
            Assert.AreEqual(frames[i],actual,$"Frame {i} semantic fields");
            File.WriteAllBytes(Path.Combine(output,$"frame-{i:00}.bytes"),FrameCodec.Encode(frames[i]));
        }
    }

    [TestMethod]
    public void RustUnsigned64VectorsPreserveNestedRepeatedMapSignedAndBinaryFields()
    {
        var output=Path.Combine(Vectors,"CSharp"); Directory.CreateDirectory(output);
        for(int i=0;i<Values.Length;i++)
        {
            var value=Values[i];
            var actual=Fixture.V1.PingRequest.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(Vectors,"Rust",$"golden-{i}.bytes")));
            Assert.AreEqual(value,actual.Max); Assert.AreEqual(Request,actual.Beyond); Assert.AreEqual(value,actual.Fixed);
            Assert.AreEqual(Binary,actual.Data); Assert.AreEqual(long.MinValue,actual.Signed);
            Assert.AreEqual(long.MinValue,actual.Zigzag); Assert.AreEqual(long.MinValue,actual.SignedFixed);
            CheckChild(actual.Child,value); Assert.AreEqual(1,actual.Children.Count); CheckChild(actual.Children[0],value);
            CollectionAssert.AreEqual(Values,actual.Values.ToArray()); CollectionAssert.AreEqual(Values,actual.FixedValues.ToArray());
            Assert.AreEqual(1,actual.Lookup.Count); CheckChild(actual.Lookup[value],value);
            Assert.AreEqual(1,actual.FixedLookup.Count); Assert.AreEqual(value,actual.FixedLookup[value]);
            var child=new Fixture.V1.Child { Value=value, Fixed=value, Signed=long.MinValue, Data=Binary };
            var own=new Fixture.V1.PingRequest { Max=value, Beyond=Request, Fixed=value, Data=Binary,
                Signed=long.MinValue,Zigzag=long.MinValue,SignedFixed=long.MinValue,Child=child };
            own.Children.Add(child.Clone()); own.Values.Add(Values); own.FixedValues.Add(Values);
            own.Lookup.Add(value,child.Clone()); own.FixedLookup.Add(value,value);
            File.WriteAllBytes(Path.Combine(output,$"golden-{i}.bytes"),own.ToByteArray());
        }
    }
    static void CheckChild(Fixture.V1.Child child,ulong value)
    { Assert.IsNotNull(child); Assert.AreEqual(value,child.Value); Assert.AreEqual(value,child.Fixed); Assert.AreEqual(long.MinValue,child.Signed); Assert.AreEqual(Binary,child.Data); }
}
