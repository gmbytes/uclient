using System;
using System.Threading;
using Client.Contracts;

namespace Client.Framework.Release
{
    /// <summary>A retained context bound to exactly one selection and readiness gate.</summary>
    internal sealed class LaunchReadinessContext : IClientContext, IDisposable
    {
        private readonly IClientContext source;
        private readonly Action<LaunchReadinessContext> requestReady;
        private bool disposed;
        internal LocalReadinessGate Gate { get; }
        internal CancellationToken OriginalLifetime { get; }
        public IResourceService Resources => source.Resources;
        public IUIService UI => source.UI;
        public ISessionClient Session => source.Session;
        public ILogService Log => source.Log;
        public CancellationToken Lifetime { get; }
        internal LaunchReadinessContext(IClientContext source, LocalReadinessGate gate, CancellationToken token, Action<LaunchReadinessContext> requestReady)
        { this.source = source; Gate = gate; Lifetime = token; OriginalLifetime = source.Lifetime; this.requestReady = requestReady; }
        public void ReportReady() { if (!Lifetime.IsCancellationRequested) requestReady(this); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Gate.Dispose();
            // Native composition can own UI/session scopes without widening stable Contracts.
            (source as IDisposable)?.Dispose();
        }
    }
}
