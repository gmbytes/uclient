using System;
namespace Client.Framework.Network
{
    public sealed class RequestIdSequence
    {
        private readonly object gate = new object();
        private ulong next;
        private bool exhausted;
        public RequestIdSequence(ulong first = 1)
        {
            if (first == 0) throw new ArgumentOutOfRangeException(nameof(first));
            next = first;
        }
        public ulong Next()
        {
            lock (gate)
            {
                if (exhausted) throw new InvalidOperationException("Request IDs exhausted; create a new session client.");
                ulong result = next;
                if (next == ulong.MaxValue) exhausted = true; else next++;
                return result;
            }
        }
    }
}
