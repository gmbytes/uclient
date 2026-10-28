using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Client.Tests.Managed;

[TestClass]
public sealed class ProtocolCompatibilityTests
{
    [TestMethod]
    public void EmptyV1BodyParsesZeroUint64ThroughBothStaticParsers()
    {
        var old=Fixture.Compatibility.V1.Request.Parser.ParseFrom(Array.Empty<byte>());
        Assert.AreEqual(0UL,old.Number);
        var modern=Fixture.Compatibility.V2.Request.Parser.ParseFrom(old.ToByteArray());
        Assert.AreEqual(0UL,modern.Number);Assert.AreEqual("",modern.Extra);
    }
    [TestMethod]
    public void V2UnknownFieldSurvivesV1AndStaticParserRoundTrip()
    {
        var modern = new Fixture.Compatibility.V2.Request { Number = ulong.MaxValue, Extra = "retained-through-v1" };
        var old = Fixture.Compatibility.V1.Request.Parser.ParseFrom(modern.ToByteArray());
        Assert.AreEqual(ulong.MaxValue, old.Number);
        var restored = Fixture.Compatibility.V2.Request.Parser.ParseFrom(old.ToByteArray());
        Assert.AreEqual(ulong.MaxValue, restored.Number);
        Assert.AreEqual("retained-through-v1", restored.Extra);
        var root = Environment.GetEnvironmentVariable("SGAME_TOK_INTEROP_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            var output = Path.Combine(root, "CSharp"); Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "compat-v2.bytes"), modern.ToByteArray());
            File.WriteAllBytes(Path.Combine(output, "compat-v1-preserved.bytes"), old.ToByteArray());
        }
    }
}
