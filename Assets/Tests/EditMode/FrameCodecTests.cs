using System;
using Client.Framework.Network;
using Google.Protobuf;
using NUnit.Framework;
using Tok.Client.V1;

namespace Client.Tests.EditMode
{
    [Category("N2")]
    public sealed class FrameCodecTests
    {
        [TestCase(0u), TestCase(1048577u), TestCase(uint.MaxValue)]
        public void RejectsInvalidUnsignedLength(uint length)
        {
            var prefix = Prefix(length);
            Assert.That(Assert.Throws<TransportException>(() => FrameCodec.ReadLength(prefix)).Code,
                Is.EqualTo(TransportError.InvalidFrame));
        }

        [Test]
        public void AcceptsExactOneMiB()
        {
            // 2 version bytes + 4 outer field bytes + 4 body field bytes.
            var frame = new Frame { Version = 3, Notify = new Notify { Body = ByteString.CopyFrom(new byte[1048566]) } };
            var encoded = FrameCodec.Encode(frame);
            Assert.That(encoded.Length, Is.EqualTo(1048580));
            Assert.That(encoded[0], Is.EqualTo(0)); Assert.That(encoded[1], Is.EqualTo(16));
            Assert.That(FrameCodec.Decode(encoded).Notify.Body.Length, Is.EqualTo(1048566));
            Assert.That(FrameCodec.ReadLength(new byte[] { 0, 16, 0, 0 }), Is.EqualTo(1048576));
        }

        [Test]
        public void CodecPreservesGoldenEnvelopeAndRejectsTailOrTruncation()
        {
            var golden = new byte[] { 0, 0, 0, 4, 8, 3, 66, 0 };
            Assert.That(FrameCodec.Encode(new Frame { Version = 3, Ack = new Ack() }), Is.EqualTo(golden));
            Assert.That(FrameCodec.Decode(golden).ContentCase, Is.EqualTo(Frame.ContentOneofCase.Ack));
            Assert.That(Assert.Throws<TransportException>(() => FrameCodec.Decode(new byte[] { 0, 0, 0, 1, 8 })).Code,
                Is.EqualTo(TransportError.InvalidFrame));
            Assert.Throws<TransportException>(() => FrameCodec.Decode(new byte[] { 0, 0, 0, 2, 8, 3, 0 }));
            Assert.Throws<TransportException>(() => FrameCodec.Decode(new byte[] { 0, 0, 0 }));
            Assert.Throws<TransportException>(() => FrameCodec.Encode(new Frame()));
        }

        internal static byte[] Prefix(uint n) => new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };
    }
}
