// HUDManager.cs — Master HUD controller for the D-Space overlay
// The Daemon's HUD glasses project a persistent heads-up display showing
// call-outs, status bars, compass, minimap, and contextual information.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.HUD
{
    /// <summary>
    /// Orchestrates all HUD elements — the visible interface of D-Space.
    /// Manages layout zones, visibility, and the rendering pipeline for AR overlays.
    /// </summary>
    public class HUDManager : SubsystemBase
    {
        public override string Name => "HUD";

        [Header("HUD Layout")]
        [SerializeField] private Canvas hudCanvas;
        [SerializeField] private RectTransform topBar;
        [SerializeField] private RectTransform bottomBar;
        [SerializeField] private RectTransform leftPanel;
        [SerializeField] private RectTransform rightPanel;
        [SerializeField] private RectTransform centerReticle;
        [SerializeField] private RectTransform notificationArea;

        [Header("HUD Settings")]
        [SerializeField] private float hudOpacity = 0.85f;
        [SerializeField] private HUDColorScheme colorScheme;
        [SerializeField] private bool showPerformanceStats;
        [SerializeField] private float nameplateMaxDistance = 50f;

        [Header("World-Space HUD")]
        [SerializeField] private float worldHUDScale = 0.01f;

        public float HUDOpacity => hudOpacity;
        public HUDColorScheme Colors => colorScheme;
        public float NameplateMaxDistance => nameplateMaxDistance;

        private readonly List<IHUDElement> hudElements = new List<IHUDElement>();
        private CanvasGroup canvasGroup;

        protected override Task OnInitialize()
        {
            if (hudCanvas == null)
            {
                // Create HUD canvas if not assigned
                var canvasGO = new GameObject("DSpace_HUD_Canvas");
                canvasGO.transform.SetParent(Manager.HUDRoot);
                hudCanvas = canvasGO.AddComponent<Canvas>();
                hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hudCanvas.sortingOrder = 100;

                canvasGroup = canvasGO.AddComponent<CanvasGroup>();
                canvasGroup.alpha = hudOpacity;

                var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                CreateHUDLayout();
            }

            if (colorScheme == null)
                colorScheme = CreateDefaultColorScheme();

            return Task.CompletedTask;
        }

        public void RegisterHUDElement(IHUDElement element)
        {
            if (!hudElements.Contains(element))
                hudElements.Add(element);
        }

        public void UnregisterHUDElement(IHUDElement element)
        {
            hudElements.Remove(element);
        }

        public void SetOpacity(float opacity)
        {
            hudOpacity = Mathf.Clamp01(opacity);
            if (canvasGroup != null)
                canvasGroup.alpha = hudOpacity;
        }

        public void ShowNotification(string message, NotificationType type, float duration = 5f)
        {
            Log($"[{type}] {message}");
            // In full implementation, this spawns a notification UI element
            // that slides in from the edge and auto-dismisses
        }

        public override void Tick(float deltaTime)
        {
            foreach (var element in hudElements)
            {
                if (element.IsVisible)
                    element.UpdateHUD(deltaTime);
            }
        }

        private void CreateHUDLayout()
        {
            topBar = CreateLayoutZone("TopBar", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -60), new Vector2(0, 0));

            bottomBar = CreateLayoutZone("BottomBar", new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(0, 60));

            leftPanel = CreateLayoutZone("LeftPanel", new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(200, 0));

            rightPanel = CreateLayoutZone("RightPanel", new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-200, 0), new Vector2(0, 0));

            centerReticle = CreateLayoutZone("CenterReticle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-25, -25), new Vector2(25, 25));

            notificationArea = CreateLayoutZone("Notifications", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-320, -200), new Vector2(-10, -10));
        }

        private RectTransform CreateLayoutZone(string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(hudCanvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private HUDColorScheme CreateDefaultColorScheme()
        {
            return new HUDColorScheme
            {
                // Daemon-inspired color palette — cool blues and cyans with hot accents
                Primary = new Color(0.0f, 0.75f, 1.0f, 1.0f),         // Cyan blue
                Secondary = new Color(0.0f, 1.0f, 0.65f, 1.0f),       // Teal green
                Accent = new Color(1.0f, 0.6f, 0.0f, 1.0f),           // Amber
                Warning = new Color(1.0f, 0.85f, 0.0f, 1.0f),         // Yellow
                Danger = new Color(1.0f, 0.15f, 0.15f, 1.0f),         // Red
                Friendly = new Color(0.2f, 0.8f, 0.2f, 1.0f),         // Green
                Neutral = new Color(0.7f, 0.7f, 0.7f, 1.0f),          // Gray
                Background = new Color(0.05f, 0.05f, 0.1f, 0.6f),     // Dark translucent
                TextPrimary = Color.white,
                TextSecondary = new Color(0.75f, 0.85f, 1.0f, 1.0f),  // Light blue-white
                NameplateFriendly = new Color(0.2f, 0.8f, 1.0f, 0.9f),
                NameplateHostile = new Color(1.0f, 0.2f, 0.2f, 0.9f),
                NameplateNeutral = new Color(0.8f, 0.8f, 0.8f, 0.9f),
                QuestThread = new Color(1.0f, 0.85f, 0.0f, 0.8f),     // Gold quest path
                ThreatOutline = new Color(1.0f, 0.0f, 0.0f, 0.8f),
            };
        }

        protected override void OnShutdown()
        {
            hudElements.Clear();
        }
    }

    public interface IHUDElement
    {
        bool IsVisible { get; }
        void UpdateHUD(float deltaTime);
        void Show();
        void Hide();
    }

    [System.Serializable]
    public class HUDColorScheme
    {
        public Color Primary;
        public Color Secondary;
        public Color Accent;
        public Color Warning;
        public Color Danger;
        public Color Friendly;
        public Color Neutral;
        public Color Background;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color NameplateFriendly;
        public Color NameplateHostile;
        public Color NameplateNeutral;
        public Color QuestThread;
        public Color ThreatOutline;
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Danger,
        Quest,
        Social,
        Economy,
        System
    }
}
