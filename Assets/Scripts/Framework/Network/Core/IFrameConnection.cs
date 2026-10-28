using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Network
{
    public interface IFrameConnection
    {
        // Runtime: TokNetworkComponent invokes events from Update on its owning main thread.
        // Managed tools/tests: TcpFrameConnection invokes events on its single reader pump.
        // Consumers finish applying a frame before returning; async state transitions must use
        // their captured dispatcher/context, rather than adding a second transport reader.
        event Action<byte[]> FrameReceived;
        event Action<TransportException> TransportFailed;
        Task ConnectAsync(string host, int port, CancellationToken token);
        Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token);
        void Close();
    }
}
