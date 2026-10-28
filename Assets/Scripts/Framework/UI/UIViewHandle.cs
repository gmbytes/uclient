using System;
using System.Collections.Generic;
using System.Threading;
using Client.Contracts;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    /// <summary>One native GF serial generation; never owns a separate prefab asset handle.</summary>
    public sealed class UIViewHandle : IViewHandle
    {
        private readonly UIService owner;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        internal UIViewBridge Bridge { get; }
        internal UIViewDefinition Definition { get; }
        internal int SerialId { get; }
        public GameObject Root => !IsClosed ? Bridge.gameObject : throw new ObjectDisposedException(nameof(UIViewHandle));
        public bool IsClosed { get; private set; }
        public CancellationToken Lifetime => token;
        public event Action Closed;

        internal UIViewHandle(UIService owner, UIViewBridge bridge, UIViewDefinition definition, int serialId)
        {
            this.owner = owner; Bridge = bridge; Definition = definition; SerialId = serialId;
            token = lifetime.Token; bridge.Closed += Invalidate;
        }

        public void Close() { if (!IsClosed) owner.Close(this); }

        /// <summary>Use for callbacks whose result is no longer relevant after closing this generation.</summary>
        public Action Guard(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return () => { if (!IsClosed) callback(); };
        }

        /// <summary>Presenter-owned subscription, also removed automatically at native close.</summary>
        public IDisposable Listen(string bindingId, UnityAction callback)
        {
            if (IsClosed) throw new ObjectDisposedException(nameof(UIViewHandle));
            var button = Bridge.Resolve(bindingId) as Button;
            if (button == null) throw new InvalidOperationException("Binding is not a Button: " + bindingId);
            return Listen(button, callback);
        }

        internal IDisposable Listen(Button button, UnityAction callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            UnityAction guarded = () => { if (!IsClosed) callback(); };
            button.onClick.AddListener(guarded);
            var subscription = new ButtonSubscription(button, guarded);
            subscriptions.Add(subscription); return subscription;
        }

        internal void Invalidate()
        {
            if (IsClosed) return;
            IsClosed = true; Bridge.Closed -= Invalidate;
            foreach (var subscription in subscriptions) subscription.Dispose();
            subscriptions.Clear();
            // Removal/order state must be settled before presenter continuations run.
            owner.Forget(this);
            try { lifetime.Cancel(); Closed?.Invoke(); }
            finally { Closed = null; lifetime.Dispose(); }
        }

        private sealed class ButtonSubscription : IDisposable
        {
            private Button button;
            private UnityAction callback;
            public ButtonSubscription(Button button, UnityAction callback) { this.button = button; this.callback = callback; }
            public void Dispose()
            {
                if (button != null) button.onClick.RemoveListener(callback);
                button = null; callback = null;
            }
        }
    }
}
