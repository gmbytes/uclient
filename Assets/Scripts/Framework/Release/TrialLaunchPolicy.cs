using System;
using System.Linq;
using System.Threading;

namespace Client.Framework.Release
{
    /// <summary>All durable read/modify/write transitions use the F5 per-root state lock.</summary>
    internal sealed class TrialLaunchPolicy
    {
        private readonly ReleaseStateStore store;
        private readonly object sync;
        internal TrialLaunchPolicy(ReleaseStateStore store) { this.store = store; sync = ReleaseFileSystem.StateLock(store.Root); }
        internal void RecoverUnfinished()
        {
            lock (sync)
            {
                var state = store.Read();
                if (state.Attempt > 0) Quarantine(state.Candidate);
            }
        }
        internal bool Begin(string id)
        {
            lock (sync)
            {
                var state = store.Read();
                if (state.Attempt > 0) throw new InvalidOperationException("An unfinished trial must be recovered before launch.");
                if (state.Candidate != id && state.KnownGood != id && state.PreviousGood != id)
                    throw new InvalidOperationException("The selected release is no longer referenced.");
                store.VerifySlot(id);
                bool trial = state.Candidate == id && state.KnownGood != id;
                if (trial) { state.Attempt = 1; store.WriteAtomic(state, CancellationToken.None, "trial", id); }
                else if (state.Candidate == id) { state.Candidate = null; store.WriteAtomic(state, CancellationToken.None, "clearCandidate", id); }
                return trial;
            }
        }
        internal void Promote(string id, CancellationToken token)
        {
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                var state = store.Read();
                if (state.BadReleaseIds.Contains(id, StringComparer.Ordinal)) throw new InvalidOperationException("A quarantined release cannot become ready.");
                if (state.KnownGood == id && state.Candidate == null && state.Attempt == 0) return;
                if (state.Candidate != id && state.KnownGood != id && state.PreviousGood != id)
                    throw new InvalidOperationException("Launch selection changed before ready commit.");
                if (state.KnownGood != id) { state.PreviousGood = state.KnownGood ?? state.PreviousGood; state.KnownGood = id; }
                if (state.PreviousGood == id) state.PreviousGood = null;
                state.Candidate = null; state.Attempt = 0; store.WriteAtomic(state, token, "promote", id);
            }
        }
        internal void Quarantine(string id)
        {
            lock (sync)
            {
                var state = store.Read();
                if (state.KnownGood == id) { state.KnownGood = state.PreviousGood == id ? null : state.PreviousGood; state.PreviousGood = null; }
                if (state.PreviousGood == id) state.PreviousGood = null;
                if (state.Candidate == id) { state.Candidate = null; state.Attempt = 0; }
                if (!state.BadReleaseIds.Contains(id, StringComparer.Ordinal)) state.BadReleaseIds = state.BadReleaseIds.Concat(new[] { id }).ToArray();
                store.WriteAtomic(state, CancellationToken.None, "quarantine", id);
            }
        }
    }
}
