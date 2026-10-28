using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Network
{
    public interface IFrameTransport
    {
        Task ConnectAsync(string host, int port, CancellationToken token);
        Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token);
        // Exactly one consumer. A returned frame keeps its receive-budget reservation until
        // the next ReadAsync call (or Close). Finish processing/delivery before requesting more.
        Task<byte[]> ReadAsync(CancellationToken token);
        void Close();
    }

    public enum TransportError { NotConnected, InvalidFrame, Backpressure, Timeout, EndOfStream, TruncatedFrame, Cancelled, Closed, SocketError }

    public sealed class TransportException : IOException
    {
        public TransportError Code { get; }
        public TransportException(TransportError code, string message, Exception inner = null) : base(message, inner) { Code = code; }
    }
}
