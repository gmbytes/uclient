using System;
using System.Threading;
using Client.Framework.Network;
using Client.Framework.UI;
using Google.Protobuf;
using Tok.Client.V1;

namespace Client.Framework.Release
{
    /// <summary>Local startup facts; server authentication is deliberately outside this gate.</summary>
    internal sealed class LocalReadinessGate : IDisposable
    {
        private readonly UIService ui;
        private readonly string expectedViewId;
        private readonly CancellationToken token;
        private UIViewHandle presented, requested;
        private bool resourceInitialized, hotfixLoaded, envelopeVerified, entrySucceeded, readyRequested, aborted, disposed;
        internal event Action Invalidated;
        internal bool CanCommit => !disposed && !aborted && !token.IsCancellationRequested && resourceInitialized && hotfixLoaded &&
            envelopeVerified && entrySucceeded && readyRequested && ReferenceEquals(requested, presented) && LiveFrame;
        private bool LiveFrame => presented != null && presented.HasPresentedFrame && ui.CanPresent(presented);

        internal LocalReadinessGate(UIService ui, string expectedViewId, CancellationToken token)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.expectedViewId = !string.IsNullOrWhiteSpace(expectedViewId) ? expectedViewId : throw new ArgumentException("An expected local view is required.", nameof(expectedViewId));
            this.token = token; ui.FirstFramePresented += OnFirstFrame;
        }
        internal void RecordResourceInitialized() { if (!aborted && !disposed) resourceInitialized = true; }
        internal void RecordHotfixLoaded() { if (!aborted && !disposed) hotfixLoaded = true; }
        internal void VerifyLocalEnvelope()
        {
            var original = new Frame { Version = 3, Request = new BusinessRequest { RequestId = ulong.MaxValue, MessageId = 101, Body = ByteString.CopyFrom(new byte[] { 8, 1, 127 }) } };
            var decoded = FrameCodec.Decode(FrameCodec.Encode(original));
            if (decoded.Version != 3 || decoded.ContentCase != Frame.ContentOneofCase.Request || decoded.Request.RequestId != ulong.MaxValue ||
                decoded.Request.MessageId != 101 || !decoded.Request.Body.Equals(original.Request.Body))
                throw new InvalidOperationException("The local canonical Protobuf envelope selfcheck failed.");
            if (!aborted && !disposed) envelopeVerified = true;
        }
        internal void EntrySucceeded() { if (!aborted && !disposed) entrySucceeded = true; }
        internal bool ReportReady()
        {
            // Reject before all pre-return facts; later facts never turn this call into a latched request.
            if (disposed || aborted || token.IsCancellationRequested || !resourceInitialized || !hotfixLoaded || !envelopeVerified || !LiveFrame) return false;
            requested = presented; readyRequested = true; return true;
        }
        private void OnFirstFrame(UIViewHandle view)
        {
            if (disposed || aborted || token.IsCancellationRequested || view.Definition.ViewID != expectedViewId || !ui.CanPresent(view)) return;
            if (presented != null) presented.Closed -= Abort;
            presented = view; presented.Closed += Abort;
        }
        internal void Abort()
        {
            if (aborted || disposed) return;
            aborted = true; readyRequested = false; Invalidated?.Invoke();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            ui.FirstFramePresented -= OnFirstFrame;
            if (presented != null) presented.Closed -= Abort;
            presented = requested = null; Invalidated = null;
        }
    }
}