using System;

namespace Client.Framework.Network
{
    public sealed class TransportOptions
    {
        public TimeSpan ConnectTimeout { get; }
        public TimeSpan ReadFrameTimeout { get; }
        public TimeSpan WriteFrameTimeout { get; }
        public int MaxQueueItems { get; }
        public int MaxQueueBytes { get; }
        public TransportOptions(TimeSpan? connectTimeout = null, TimeSpan? readFrameTimeout = null,
            TimeSpan? writeFrameTimeout = null, int maxQueueItems = 64, int maxQueueBytes = 4194304)
        {
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
            ReadFrameTimeout = readFrameTimeout ?? TimeSpan.FromSeconds(15);
            WriteFrameTimeout = writeFrameTimeout ?? TimeSpan.FromSeconds(15);
            MaxQueueItems = maxQueueItems; MaxQueueBytes = maxQueueBytes;
            if (ConnectTimeout <= TimeSpan.Zero || ReadFrameTimeout <= TimeSpan.Zero || WriteFrameTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(connectTimeout), "Timeouts must be positive.");
            if (maxQueueItems < 1 || maxQueueItems > 64 || maxQueueBytes < 5 || maxQueueBytes > 4194304)
                throw new ArgumentOutOfRangeException(nameof(maxQueueItems), "Queue limits must fit 64 frames and 4 MiB.");
        }
    }
}
