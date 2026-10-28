using Google.Protobuf;

namespace Client.Framework.Protocol
{
    public interface IMessageRegistry
    {
        bool TryGetParser(uint messageId, out MessageParser parser);
        bool TryGetResponseId(uint requestId, out uint responseId);
    }
}
