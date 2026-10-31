using System;
using Tok.Client.V1;
using Google.Protobuf;
using System.IO;

namespace Client.Framework.Network
{
    /// <summary>固定 Tok 信封的传输边界：四字节网络序长度不含前缀，正文在分配前限制为 1 MiB。</summary>
    public static class FrameCodec
    {
        public const int MaxBodyLength = 1048576;
        public static byte[] Encode(Frame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            int length = frame.CalculateSize();
            ValidateLength((uint)length);
            var result = new byte[length + 4];
            result[0] = (byte)(length >> 24); result[1] = (byte)(length >> 16);
            result[2] = (byte)(length >> 8); result[3] = (byte)length;
            using (var stream = new MemoryStream(result, 4, length, true))
            using (var output = new CodedOutputStream(stream)) frame.WriteTo(output);
            return result;
        }

        public static Frame Decode(ReadOnlySpan<byte> framed)
        {
            if (framed.Length < 4) throw Invalid("Missing four-byte length prefix.");
            int length = ReadLength(framed.Slice(0, 4));
            if (framed.Length != length + 4) throw Invalid("Expected exactly one complete frame.");
            try { return Frame.Parser.ParseFrom(framed.Slice(4).ToArray()); }
            catch (InvalidProtocolBufferException ex) { throw new TransportException(TransportError.InvalidFrame, "Malformed Protobuf frame.", ex); }
        }

        public static int ReadLength(ReadOnlySpan<byte> prefix)
        {
            if (prefix.Length != 4) throw Invalid("A length prefix must contain four bytes.");
            uint length = ((uint)prefix[0] << 24) | ((uint)prefix[1] << 16) | ((uint)prefix[2] << 8) | prefix[3];
            ValidateLength(length);
            return (int)length;
        }

        private static void ValidateLength(uint length)
        {
            if (length == 0 || length > MaxBodyLength) throw Invalid("Body length must be between 1 and 1048576 bytes.");
        }
        private static TransportException Invalid(string message) => new TransportException(TransportError.InvalidFrame, message);
    }
}
