using System;
using Client.Framework.Network;
using NUnit.Framework;
using Tok.Client.V1;
namespace Client.Tests
{
    [Category("N3")]
    public sealed class RequestIdTests
    {
        [Test] public void RequestIdsAbove2Pow53RemainExact()
        {
            var sequence = new RequestIdSequence(9007199254740993UL);
            var framed = FrameCodec.Encode(new Frame { Version = 3, Login = new LoginRequest { RequestId = sequence.Next(), Credential = "demo:1" } });
            Assert.That(FrameCodec.Decode(framed).Login.RequestId, Is.EqualTo(9007199254740993UL));
            Assert.That(sequence.Next(), Is.EqualTo(9007199254740994UL));
        }
        [Test] public void OverflowRequiresNewSession()
        {
            var sequence = new RequestIdSequence(ulong.MaxValue);
            Assert.That(sequence.Next(), Is.EqualTo(ulong.MaxValue));
            Assert.Throws<InvalidOperationException>(() => sequence.Next());
            Assert.Throws<InvalidOperationException>(() => sequence.Next());
            Assert.Throws<ArgumentOutOfRangeException>(() => new RequestIdSequence(0));
        }
    }
}
