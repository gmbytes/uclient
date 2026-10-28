using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using UnityEngine;

namespace Client.Framework.UI
{
    /// <summary>Waits for a future render of a live, laid-out native GF window.</summary>
    public static class UIFirstFrame
    {
        public static Task WaitAsync(IViewHandle view, CancellationToken token)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!(view is UIViewHandle concrete)) throw new ArgumentException("A live UIService-owned view is required.", nameof(view));
            return concrete.WaitForFirstFrameAsync(token);
        }
    }

    // Each observation belongs to one handle/serial, never to a pooled Bridge generation.
    internal sealed class FirstFrameObservation
    {
        private readonly UIService owner;
        private readonly UIViewHandle view;
        private readonly CancellationTokenSource cancellation;
        private readonly CancellationToken token;
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        private readonly int armedFrame;
        private CancellationTokenRegistration registration;
        private Coroutine coroutine;
        private int renderFrame = -1;
        private ScreenLayoutSnapshot renderedSnapshot;
        private bool cleaned;

        internal FirstFrameObservation(UIService owner, UIViewHandle view, CancellationToken token)
        {
            this.owner = owner; this.view = view; armedFrame = Time.frameCount;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, view.Lifetime);
            this.token = cancellation.Token;
        }

        internal Task Start()
        {
            registration = token.Register(() => owner.OnMainThread(Cancel));
            if (completion.Task.IsCompleted) { registration.Dispose(); return completion.Task; }
            Canvas.willRenderCanvases += OnWillRender;
            view.Bridge.VisibilityChanged += OnVisibilityChanged;
            try { coroutine = owner.StartFrameObservation(Observe()); }
            catch (Exception error) { Cleanup(false); completion.TrySetException(error); }
            return completion.Task;
        }

        private void OnWillRender()
        {
            if (token.IsCancellationRequested || Time.frameCount <= armedFrame || !owner.CanPresent(view)) return;
            renderFrame = Time.frameCount;
            renderedSnapshot = view.Bridge.AppliedSnapshot;
        }

        private void OnVisibilityChanged(bool visible)
        {
            // A pause/resume after a canvas callback invalidates that callback's evidence too.
            renderFrame = -1; renderedSnapshot = null;
        }

        private IEnumerator Observe()
        {
            while (!completion.Task.IsCompleted)
            {
                yield return null;
                // Editor batch mode deliberately has no successful fallback for this boundary.
                yield return new WaitForEndOfFrame();
                if (token.IsCancellationRequested) { Cancel(); yield break; }
                if (renderFrame != Time.frameCount || renderFrame <= armedFrame ||
                    !ReferenceEquals(renderedSnapshot, view.Bridge.AppliedSnapshot)) continue;
                bool confirmed;
                try { confirmed = owner.ConfirmFirstFrame(view); }
                catch (Exception error) { Cleanup(false); completion.TrySetException(error); yield break; }
                if (!confirmed) continue;
                Cleanup(false);
                completion.TrySetResult(true);
            }
        }

        private void Cancel()
        {
            Cleanup(true);
            completion.TrySetCanceled();
        }

        private void Cleanup(bool stop)
        {
            if (cleaned) return; cleaned = true;
            Canvas.willRenderCanvases -= OnWillRender;
            if (view.Bridge != null) view.Bridge.VisibilityChanged -= OnVisibilityChanged;
            if (stop && coroutine != null) owner.StopFrameObservation(coroutine);
            coroutine = null; registration.Dispose(); cancellation.Dispose();
        }
    }
}
