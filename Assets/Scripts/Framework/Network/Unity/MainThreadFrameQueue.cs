using System;
using System.Collections.Generic;

namespace Client.Framework.Network
{
    public sealed class MainThreadFrameQueue
    {
        private readonly object gate = new object();
        private readonly Queue<byte[]> frames = new Queue<byte[]>();
        private readonly TransportOptions options;
        private long epoch;
        private int bytes;
        public MainThreadFrameQueue(TransportOptions options = null) { this.options = options ?? new TransportOptions(); }
        public void BeginEpoch(long value)
        {
            lock (gate) { epoch = value; frames.Clear(); bytes = 0; }
        }
        public bool TryEnqueue(long value, byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            lock (gate)
            {
                if (value != epoch || frames.Count >= options.MaxQueueItems || frame.Length > options.MaxQueueBytes - bytes) return false;
                frames.Enqueue(frame); bytes += frame.Length; return true;
            }
        }
        public int Drain(long value, Action<byte[]> receive)
        {
            if (receive == null) throw new ArgumentNullException(nameof(receive));
            int delivered = 0;
            // Bound one Update's work even if a producer keeps filling the queue.
            int budget; lock (gate) budget = frames.Count;
            while (delivered < budget)
            {
                byte[] frame;
                lock (gate)
                {
                    if (value != epoch || frames.Count == 0) break;
                    frame = frames.Dequeue(); bytes -= frame.Length;
                }
                receive(frame); delivered++;
            }
            return delivered;
        }
        public int Count { get { lock (gate) return frames.Count; } }
        public int Bytes { get { lock (gate) return bytes; } }
    }
}
