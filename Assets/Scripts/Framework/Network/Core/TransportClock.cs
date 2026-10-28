using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Client.Framework.Network
{
    public interface ITransportClock
    {
        TimeSpan Elapsed { get; }
        Task Delay(TimeSpan duration, CancellationToken token);
    }

    public sealed class TransportClock : ITransportClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public TimeSpan Elapsed => watch.Elapsed;
        public Task Delay(TimeSpan duration, CancellationToken token) => Task.Delay(duration, token);
    }
}
