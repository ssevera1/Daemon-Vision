// DSpaceUIManager.cs — Master UI controller that builds the entire D-Space HUD programmatically
// The Daemon's HUD is a persistent screen-space overlay: status bars, compass, minimap,
// chat, quest tracker, notifications, and a center reticle — all rendered at runtime.

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
    /// Master UI controller that builds every HUD zone programmatically and
    /// orchestrates visibility, panel toggling, and fade animations.
    /// Subscribes to DSpaceManager.OnDSpaceReady to populate live data.
    /// </summary>
    public class DSpaceUIManager : MonoBehaviour, IHUDElement
    {
        // ── Singleton ──────────────────────────────────────────────
        public static DSpaceUIManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────
        [Header("Fade Settings")]
        [SerializeField] private float fadeInDuration = 0.6f;
        [SerializeField] private float fadeOutDuration = 0.4f;

        // ── IHUDElement ────────────────────────────────────────────
        public bool IsVisible { get; private set; }

        // ── Runtime references ─────────────────────────────────────
        private Canvas hudCanvas;
        private CanvasScaler canvasScaler;
        private CanvasGroup canvasGroup;
        private GraphicRaycaster raycaster;

        // Layout zone transforms
        private RectTransform topStatusBarZone;
        private RectTransform bottomStatusBarZone;
        private RectTransform leftPanelZone;
        private RectTransform rightPanelZone;
        private RectTransform centerReticleZone;
        private RectTransform notificationPanelZone;
        private RectTransform minimapPanelZone;
        private RectTransform compassStripZone;
        private RectTransform chatPanelZone;
        private RectTransform questTrackerZone;

        // Panel lookup for Show/Hide/Toggle
        private readonly Dictionary<string, RectTransform> panels = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, CanvasGroup> panelGroups = new Dictionary<string, CanvasGroup>();

        // ── Lifecycle ──────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (DSpaceManager.Instance != null)
            {
                DSpaceManager.Instance.OnDSpaceReady += OnDSpaceReady;
                if (DSpaceManager.Instance.State == DSpaceState.Online)
                    OnDSpaceReady();
            }
        }

        private void OnDisable()
        {
            if (DSpaceManager.Instance != null)
                DSpaceManager.Instance.OnDSpaceReady -= OnDSpaceReady;
        }

        private void Start()
        {
            BuildCanvas();
            BuildLayoutZones();
            RegisterAllPanels();

            // Start hidden, fade in when D-Space is ready
            canvasGroup.alpha = 0f;
            IsVisible = false;
        }

        // ── Canvas Construction ────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("DSpace_UI_Canvas");
            canvasGO.transform.SetParent(transform, false);

            hudCanvas = canvasGO.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 200; // Above HUDManager's canvas

            canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            raycaster = canvasGO.AddComponent<GraphicRaycaster>();
        }

        // ── Layout Zones ───────────────────────────────────────────

        private void BuildLayoutZones()
        {
            // TopStatusBar — full width, 50px tall, pinned to top
            topStatusBarZone = CreateZone("TopStatusBar",
                new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -50), new Vector2(0, 0));

            // CompassStrip — 600px wide, 30px tall, centered just below top bar
            compassStripZone = CreateZone("CompassStrip",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                Vector2.zero, Vector2.zero);
            compassStripZone.sizeDelta = new Vector2(600, 30);
            compassStripZone.anchoredPosition = new Vector2(0, -65);

            // BottomStatusBar — full width, 40px tall, pinned to bottom
            bottomStatusBarZone = CreateZone("BottomStatusBar",
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(0, 40));

            // LeftPanel — 200px wide, full height minus bars
            leftPanelZone = CreateZone("LeftPanel",
                new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(0, 40), new Vector2(200, -50));

            // RightPanel — 280px wide, full height minus bars
            rightPanelZone = CreateZone("RightPanel",
                new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-280, 40), new Vector2(0, -50));

            // CenterReticle — 40x40 dead center
            centerReticleZone = CreateZone("CenterReticle",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            centerReticleZone.sizeDelta = new Vector2(40, 40);

            // NotificationPanel — 320px wide, top-right area
            notificationPanelZone = CreateZone("NotificationPanel",
                new Vector2(1, 1), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            notificationPanelZone.sizeDelta = new Vector2(320, 400);
            notificationPanelZone.anchoredPosition = new Vector2(-10, -60);
            notificationPanelZone.pivot = new Vector2(1, 1);

            // MinimapPanel — 150x150, bottom-left
            minimapPanelZone = CreateZone("MinimapPanel",
                new Vector2(0, 0), new Vector2(0, 0),
                Vector2.zero, Vector2.zero);
            minimapPanelZone.sizeDelta = new Vector2(150, 150);
            minimapPanelZone.anchoredPosition = new Vector2(20, 55);
            minimapPanelZone.pivot = new Vector2(0, 0);

            // ChatPanel — 350x250, bottom-right above bottom bar
            chatPanelZone = CreateZone("ChatPanel",
                new Vector2(1, 0), new Vector2(1, 0),
                Vector2.zero, Vector2.zero);
            chatPanelZone.sizeDelta = new Vector2(350, 250);
            chatPanelZone.anchoredPosition = new Vector2(-10, 50);
            chatPanelZone.pivot = new Vector2(1, 0);

            // QuestTracker — 280px wide, right side
            questTrackerZone = CreateZone("QuestTracker",
                new Vector2(1, 1), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            questTrackerZone.sizeDelta = new Vector2(280, 350);
            questTrackerZone.anchoredPosition = new Vector2(-10, -110);
            questTrackerZone.pivot = new Vector2(1, 1);
        }

        private RectTransform CreateZone(string zoneName, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(zoneName);
            go.transform.SetParent(hudCanvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            return rt;
        }

        private void RegisterAllPanels()
        {
            RegisterPanel("TopStatusBar", topStatusBarZone);
            RegisterPanel("BottomStatusBar", bottomStatusBarZone);
            RegisterPanel("LeftPanel", leftPanelZone);
            RegisterPanel("RightPanel", rightPanelZone);
            RegisterPanel("CenterReticle", centerReticleZone);
            RegisterPanel("NotificationPanel", notificationPanelZone);
            RegisterPanel("MinimapPanel", minimapPanelZone);
            RegisterPanel("CompassStrip", compassStripZone);
            RegisterPanel("ChatPanel", chatPanelZone);
            RegisterPanel("QuestTracker", questTrackerZone);
        }

        private void RegisterPanel(string key, RectTransform rt)
        {
            panels[key] = rt;
            panelGroups[key] = rt.GetComponent<CanvasGroup>();
        }

        // ── D-Space Ready ──────────────────────────────────────────

        private void OnDSpaceReady()
        {
            Debug.Log("[DSpaceUI] D-Space ready — populating HUD.");

            // Register with HUDManager so it ticks us
            var hudMgr = DSpaceManager.Instance.GetSubsystem<HUDManager>();
            hudMgr?.RegisterHUDElement(this);

            Show();
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void UpdateHUD(float deltaTime) { /* child panels self-update */ }

        public void Show()
        {
            if (IsVisible) return;
            IsVisible = true;
            StopAllCoroutines();
            StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 1f, fadeInDuration));
        }

        public void Hide()
        {
            if (!IsVisible) return;
            IsVisible = false;
            StopAllCoroutines();
            StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 0f, fadeOutDuration));
        }

        // ── Panel API ──────────────────────────────────────────────

        public void ShowPanel(string panelName)
        {
            if (!panelGroups.TryGetValue(panelName, out var cg)) return;
            StopCoroutine(nameof(FadePanelCoroutine));
            StartCoroutine(FadePanelCoroutine(cg, 1f, fadeInDuration));
        }

        public void HidePanel(string panelName)
        {
            if (!panelGroups.TryGetValue(panelName, out var cg)) return;
            StartCoroutine(FadePanelCoroutine(cg, 0f, fadeOutDuration));
        }

        public void TogglePanel(string panelName)
        {
            if (!panelGroups.TryGetValue(panelName, out var cg)) return;
            bool visible = cg.alpha > 0.5f;
            if (visible) HidePanel(panelName);
            else ShowPanel(panelName);
        }

        /// <summary>
        /// Get the RectTransform for a named zone so child UI scripts can parent themselves.
        /// </summary>
        public RectTransform GetZone(string zoneName)
        {
            panels.TryGetValue(zoneName, out var rt);
            return rt;
        }

        // ── Fade Coroutines ────────────────────────────────────────

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
        {
            float elapsed = 0f;
            cg.alpha = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            cg.alpha = to;
            cg.interactable = to > 0.5f;
            cg.blocksRaycasts = to > 0.5f;
        }

        private IEnumerator FadePanelCoroutine(CanvasGroup cg, float target, float duration)
        {
            yield return FadeCanvasGroup(cg, cg.alpha, target, duration);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
