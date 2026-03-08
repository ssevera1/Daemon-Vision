// NotificationToastUI.cs — Toast notification system for D-Space
// Slides in from the top-right, auto-dismisses, colour-coded by type,
// queues multiple notifications with max 3 visible simultaneously.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;

namespace DaemonVision.UI
{
    /// <summary>
    /// Notification toast system — top-right slide-in toasts.
    /// Up to 3 visible simultaneously; additional notifications queue.
    /// Colour-coded by NotificationType: info=cyan, success=green,
    /// warning=yellow, danger=red, quest=gold.
    /// </summary>
    public class NotificationToastUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        private const int   MaxVisible        = 3;
        private const float DefaultDuration   = 5f;
        private const float SlideDistance      = 340f;
        private const float SlideDuration      = 0.35f;
        private const float FadeDuration       = 0.25f;
        private const float ToastWidth         = 310f;
        private const float ToastHeight        = 60f;
        private const float ToastSpacing       = 6f;

        // ── Type Colours ───────────────────────────────────────────
        private static readonly Dictionary<NotificationType, Color> TypeColors
            = new Dictionary<NotificationType, Color>
        {
            { NotificationType.Info,    new Color(0f, 0.85f, 1f, 1f) },
            { NotificationType.Success, new Color(0.2f, 0.9f, 0.3f, 1f) },
            { NotificationType.Warning, new Color(1f, 0.85f, 0f, 1f) },
            { NotificationType.Danger,  new Color(1f, 0.25f, 0.2f, 1f) },
            { NotificationType.Quest,   new Color(1f, 0.85f, 0f, 1f) },
            { NotificationType.Social,  new Color(0.5f, 0.7f, 1f, 1f) },
            { NotificationType.Economy, new Color(0f, 1f, 0.65f, 1f) },
            { NotificationType.System,  new Color(0.7f, 0.75f, 0.8f, 1f) },
        };

        private static readonly Dictionary<NotificationType, string> TypeIcons
            = new Dictionary<NotificationType, string>
        {
            { NotificationType.Info,    "\u24D8" },  // i
            { NotificationType.Success, "\u2713" },  // check
            { NotificationType.Warning, "\u26A0" },  // warning
            { NotificationType.Danger,  "\u2716" },  // x
            { NotificationType.Quest,   "\u2691" },  // flag
            { NotificationType.Social,  "\u263A" },  // smiley
            { NotificationType.Economy, "\u25C8" },  // diamond
            { NotificationType.System,  "\u2699" },  // gear
        };

        private static readonly Color BgColor   = new Color(0.03f, 0.03f, 0.06f, 0.85f);
        private static readonly Color TimeColor = new Color(0.5f, 0.55f, 0.6f, 0.7f);

        // ── Runtime refs ───────────────────────────────────────────
        private RectTransform container;

        private readonly Queue<ToastRequest> pendingQueue = new Queue<ToastRequest>();
        private readonly List<ActiveToast> activeToasts   = new List<ActiveToast>();

        // ── Lifecycle ──────────────────────────────────────────────

        private void Start()
        {
            container = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            if (DSpaceManager.Instance != null)
            {
                DSpaceManager.Instance.OnDSpaceReady += OnDSpaceReady;
                if (DSpaceManager.Instance.State == DSpaceState.Online)
                    OnDSpaceReady();
            }
        }

        private void OnDestroy()
        {
            if (DSpaceManager.Instance != null)
                DSpaceManager.Instance.OnDSpaceReady -= OnDSpaceReady;
        }

        private void OnDSpaceReady()
        {
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
        }

        // ── Public API ─────────────────────────────────────────────

        /// <summary>
        /// Enqueue a notification toast.
        /// </summary>
        public void ShowNotification(string message, NotificationType type,
            float duration = DefaultDuration)
        {
            var request = new ToastRequest
            {
                Message  = message,
                Type     = type,
                Duration = duration
            };

            if (activeToasts.Count < MaxVisible)
                SpawnToast(request);
            else
                pendingQueue.Enqueue(request);
        }

        /// <summary>Convenience overload with string type.</summary>
        public void ShowNotification(string message, string typeStr, float duration = DefaultDuration)
        {
            if (Enum.TryParse<NotificationType>(typeStr, true, out var t))
                ShowNotification(message, t, duration);
            else
                ShowNotification(message, NotificationType.Info, duration);
        }

        // ── Update ─────────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            // Tick down active toasts
            for (int i = activeToasts.Count - 1; i >= 0; i--)
            {
                var toast = activeToasts[i];
                toast.RemainingTime -= deltaTime;
                if (toast.RemainingTime <= 0f && !toast.Dismissing)
                    StartCoroutine(DismissToast(toast));
            }

            // Dequeue pending
            while (pendingQueue.Count > 0 && activeToasts.Count < MaxVisible)
                SpawnToast(pendingQueue.Dequeue());
        }

        // ── Spawn / Dismiss ────────────────────────────────────────

        private void SpawnToast(ToastRequest req)
        {
            // Determine slot Y
            int slot = activeToasts.Count;
            float yOffset = -(slot * (ToastHeight + ToastSpacing));

            // Build toast GO
            var go = new GameObject($"Toast_{activeToasts.Count}");
            go.transform.SetParent(container, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(ToastWidth, ToastHeight);
            rt.anchoredPosition = new Vector2(SlideDistance, yOffset); // start off-screen right

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // Background
            var bg = go.AddComponent<Image>();
            bg.color = BgColor;
            bg.raycastTarget = false;

            // Accent bar on the left
            Color accentColor = TypeColors.TryGetValue(req.Type, out var tc) ? tc : Color.cyan;
            var accent = CreateImage(go.transform, "Accent", accentColor, 4, ToastHeight);
            accent.rectTransform.anchorMin = new Vector2(0, 0);
            accent.rectTransform.anchorMax = new Vector2(0, 1);
            accent.rectTransform.pivot = new Vector2(0, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(4, 0);

            // Icon
            string iconChar = TypeIcons.TryGetValue(req.Type, out var ic) ? ic : "\u24D8";
            var icon = CreateTMP(go.transform, "Icon", iconChar, 18, accentColor,
                TextAlignmentOptions.Center);
            var iconRT = icon.rectTransform;
            iconRT.anchorMin = new Vector2(0, 0);
            iconRT.anchorMax = new Vector2(0, 1);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.anchoredPosition = new Vector2(12, 0);
            iconRT.sizeDelta = new Vector2(28, 0);

            // Message text
            var msg = CreateTMP(go.transform, "Message", req.Message, 12,
                Color.white, TextAlignmentOptions.MidlineLeft);
            msg.overflowMode = TextOverflowModes.Ellipsis;
            msg.maxVisibleLines = 2;
            var msgRT = msg.rectTransform;
            msgRT.anchorMin = new Vector2(0, 0);
            msgRT.anchorMax = new Vector2(1, 1);
            msgRT.offsetMin = new Vector2(44, 4);
            msgRT.offsetMax = new Vector2(-8, -4);

            // Timestamp
            string now = DateTime.Now.ToString("HH:mm:ss");
            var ts = CreateTMP(go.transform, "Time", now, 9, TimeColor,
                TextAlignmentOptions.BottomRight);
            var tsRT = ts.rectTransform;
            tsRT.anchorMin = new Vector2(1, 0);
            tsRT.anchorMax = new Vector2(1, 0);
            tsRT.pivot = new Vector2(1, 0);
            tsRT.anchoredPosition = new Vector2(-8, 4);
            tsRT.sizeDelta = new Vector2(60, 14);

            var toast = new ActiveToast
            {
                Root          = go,
                RectTransform = rt,
                CanvasGroup   = cg,
                RemainingTime = req.Duration,
                SlotIndex     = slot,
                Dismissing    = false
            };
            activeToasts.Add(toast);

            StartCoroutine(SlideIn(toast));
        }

        private IEnumerator SlideIn(ActiveToast toast)
        {
            var rt = toast.RectTransform;
            var cg = toast.CanvasGroup;
            float startX = SlideDistance;
            float endX = 0f;
            float t = 0f;

            while (t < SlideDuration)
            {
                t += Time.unscaledDeltaTime;
                float frac = EaseOutCubic(t / SlideDuration);
                float x = Mathf.Lerp(startX, endX, frac);
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
                cg.alpha = frac;
                yield return null;
            }
            rt.anchoredPosition = new Vector2(endX, rt.anchoredPosition.y);
            cg.alpha = 1f;
        }

        private IEnumerator DismissToast(ActiveToast toast)
        {
            toast.Dismissing = true;

            // Fade + slide out
            var rt = toast.RectTransform;
            var cg = toast.CanvasGroup;
            float startX = rt.anchoredPosition.x;
            float endX = SlideDistance;
            float t = 0f;

            while (t < FadeDuration)
            {
                t += Time.unscaledDeltaTime;
                float frac = t / FadeDuration;
                rt.anchoredPosition = new Vector2(
                    Mathf.Lerp(startX, endX, frac), rt.anchoredPosition.y);
                cg.alpha = 1f - frac;
                yield return null;
            }

            activeToasts.Remove(toast);
            Destroy(toast.Root);

            // Re-position remaining toasts
            for (int i = 0; i < activeToasts.Count; i++)
            {
                float targetY = -(i * (ToastHeight + ToastSpacing));
                activeToasts[i].SlotIndex = i;
                StartCoroutine(AnimateY(activeToasts[i].RectTransform, targetY, 0.2f));
            }
        }

        private IEnumerator AnimateY(RectTransform rt, float targetY, float dur)
        {
            float startY = rt.anchoredPosition.y;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float y = Mathf.Lerp(startY, targetY, EaseOutCubic(t / dur));
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
                yield return null;
            }
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, targetY);
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void Show()  { IsVisible = true;  gameObject.SetActive(true); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

        // ── Easing ─────────────────────────────────────────────────

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            t -= 1f;
            return t * t * t + 1f;
        }

        // ── Helpers ────────────────────────────────────────────────

        private static Image CreateImage(Transform parent, string name, Color color,
            float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            return img;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name,
            string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align; tmp.enableAutoSizing = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ── Inner types ────────────────────────────────────────────

        private struct ToastRequest
        {
            public string Message;
            public NotificationType Type;
            public float Duration;
        }

        private class ActiveToast
        {
            public GameObject Root;
            public RectTransform RectTransform;
            public CanvasGroup CanvasGroup;
            public float RemainingTime;
            public int SlotIndex;
            public bool Dismissing;
        }
    }
}
