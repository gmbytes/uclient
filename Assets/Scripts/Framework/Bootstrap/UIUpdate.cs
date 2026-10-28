using System;
using Client.Framework.Release;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Client.Bootstrap
{
    /// <summary>Small non-obscuring AOT startup status; hidden only after a durable ready commit.</summary>
    public sealed class UIUpdate : MonoBehaviour
    {
        [SerializeField] private Text m_Status;
        private Button retryButton, exitButton;
        private UnityAction retryAction, exitAction;
        private Transform content;
        public void ConfigureActions(Action retry, Action exit)
        {
            EnsurePresentation();
            if (retryAction != null) retryButton.onClick.RemoveListener(retryAction);
            if (exitAction != null) exitButton.onClick.RemoveListener(exitAction);
            retryAction = () => retry?.Invoke(); exitAction = () => exit?.Invoke();
            retryButton.onClick.AddListener(retryAction); exitButton.onClick.AddListener(exitAction);
            ShowActions(false, false);
        }
        private void EnsurePresentation()
        {
            if (retryButton != null) return;
            // Older native builders create only a Canvas and status label. Use the same U4 policy there too.
            var safe = m_Status != null ? m_Status.GetComponentInParent<SafeAreaRoot>() : null;
            if (safe == null)
            {
                var safeObject = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaRoot));
                safeObject.transform.SetParent(transform, false); safe = safeObject.GetComponent<SafeAreaRoot>();
                var safeRect = (RectTransform)safeObject.transform; safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
                safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
                if (m_Status != null) m_Status.transform.SetParent(safeObject.transform, false);
            }
            content = safe.transform;
            var adaptive = GetComponent<AdaptiveCanvas>();
            if (adaptive == null) adaptive = gameObject.AddComponent<AdaptiveCanvas>();
            adaptive.Configure(safe, null, null);
            var canvas = GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = 100000;
            if (m_Status != null)
            {
                var rect = m_Status.rectTransform;
                rect.anchorMin = new Vector2(0,1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f,1);
                rect.sizeDelta = new Vector2(-32,80); rect.anchoredPosition = new Vector2(0,-8);
                m_Status.raycastTarget = false; m_Status.fontSize = 26;
            }
            retryButton = MakeButton("Retry", -130); exitButton = MakeButton("Exit", 130);
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        }
        private Button MakeButton(string label, float x)
        {
            var obj = new GameObject(label,typeof(RectTransform),typeof(Image),typeof(Button));obj.transform.SetParent(content,false);
            var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(230,64);rect.anchoredPosition=new Vector2(x,-92);
            obj.GetComponent<Image>().color=new Color(.08f,.12f,.18f,.9f);
            var child=new GameObject("Label",typeof(RectTransform),typeof(Text));child.transform.SetParent(obj.transform,false);
            var text=child.GetComponent<Text>();text.text=label;text.alignment=TextAnchor.MiddleCenter;text.fontSize=26;text.color=Color.white;text.raycastTarget=false;
            text.font=m_Status!=null?m_Status.font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var childRect=(RectTransform)child.transform;childRect.anchorMin=Vector2.zero;childRect.anchorMax=Vector2.one;childRect.offsetMin=childRect.offsetMax=Vector2.zero;
            return obj.GetComponent<Button>();
        }
        public void SetStatus(string message, bool isError)
        { if (m_Status == null) return; m_Status.text = message; m_Status.color = isError ? Color.red : Color.white; }
        public void SetPhase(ReleaseDiagnostics diagnostics, bool canRetry)
        {
            EnsurePresentation(); gameObject.SetActive(true);
            string text;
            switch (diagnostics.Phase)
            {
                case ReleasePhase.Check: text = "Checking for updates"; break;
                case ReleasePhase.Prepare: text = "Preparing update"; break;
                case ReleasePhase.LoginReady: text = "Opening login"; break;
                case ReleasePhase.PreparationFailed: text = "Update unavailable"; break;
                case ReleasePhase.RestartRequired: text = "Restart the application"; break;
                case ReleasePhase.Ready: text = "Ready"; break;
                case ReleasePhase.Stopped: text = "Startup stopped"; break;
                default: text = "Starting client"; break;
            }
            if (diagnostics.Phase == ReleasePhase.Prepare && diagnostics.Progress != null && diagnostics.Progress.TotalBytes > 0)
                text += " " + Mathf.Clamp01((float)diagnostics.Progress.CompletedBytes / diagnostics.Progress.TotalBytes).ToString("P0");
            bool error = diagnostics.Phase == ReleasePhase.PreparationFailed || diagnostics.Phase == ReleasePhase.RestartRequired;
            SetStatus(text,error); ShowActions(error,canRetry,error || diagnostics.Phase == ReleasePhase.Check || diagnostics.Phase == ReleasePhase.Prepare);
        }
        public void ShowFailure(string message, bool canRetry)
        { EnsurePresentation();gameObject.SetActive(true);SetStatus(message,true);ShowActions(true,canRetry); }
        private void ShowActions(bool show, bool canRetry, bool canExit = true)
        { if(retryButton!=null){retryButton.gameObject.SetActive(show);retryButton.interactable=canRetry;}if(exitButton!=null)exitButton.gameObject.SetActive(canExit); }
        private void OnDestroy()
        {if(retryButton!=null&&retryAction!=null)retryButton.onClick.RemoveListener(retryAction);if(exitButton!=null&&exitAction!=null)exitButton.onClick.RemoveListener(exitAction);}
    }
}
