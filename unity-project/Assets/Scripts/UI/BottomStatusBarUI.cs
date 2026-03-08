// BottomStatusBarUI.cs — Bottom status bar: mesh status, D-Space state, GPS & battery
// Mirrors the Daemon's persistent network-health readout at the bottom of the HUD.

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;
using DaemonVision.Network;
using DaemonVision.Spatial;

namespace DaemonVision.UI
{
    /// <summary>
    /// Bottom status bar — 40 px tall, full width.
    /// Left: mesh peer count + connection dot.  Center: D-SPACE ONLINE/OFFLINE.
    /// Right: GPS accuracy, battery level.
    /// </summary>
    public class BottomStatusBarUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor        = new Color(0.02f, 0.02f, 0.05f, 0.7f);
        private static readonly Color OnlineGreen    = new Color(0.2f, 1f, 0.4f, 1f);
        private static readonly Color OfflineRed     = new Color(1f, 0.25f, 0.25f, 1f);
        private static readonly Color LabelColor     = new Color(0.6f, 0.7f, 0.8f, 1f);
        private static readonly Color ValueColor     = new Color(0.9f, 0.95f, 1f, 1f);
        private static readonly Color DSpaceCyan     = new Color(0f, 0.85f, 1f, 1f);

        // ── Runtime refs ───────────────────────────────────────────
        private Image background;
        private Image connectionDot;
        private TextMeshProUGUI meshStatusText;
        private TextMeshProUGUI dspaceStateText;
        private TextMeshProUGUI gpsText;
        private TextMeshProUGUI batteryText;

        // Data sources
        private MeshNetworkManager meshNetwork;
        private GPSLocationProvider gpsProvider;
        private StatusBarRenderer statusRenderer;

        private float updateTimer;
        private const float UpdateInterval = 0.5f;

        // ── Lifecycle ──────────────────────────────────────────────

        private void Start()
        {
            BuildUI();

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
            meshNetwork    = DSpaceManager.Instance.GetSubsystem<MeshNetworkManager>();
            gpsProvider    = DSpaceManager.Instance.GetSubsystem<GPSLocationProvider>();
            statusRenderer = DSpaceManager.Instance.GetSubsystem<StatusBarRenderer>();

            var hudMgr = DSpaceManager.Instance.GetSubsystem<HUDManager>();
            hudMgr?.RegisterHUDElement(this);

            RefreshDisplay();
        }

        // ── UI Construction ────────────────────────────────────────

        private void BuildUI()
        {
            var root = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            StretchFill(root);

            // Background
            background = AddImage(root, "BG", BgColor);
            StretchFill(background.rectTransform);

            // ── Left: MESH status + dot ────────────────────────────
            var leftGO = new GameObject("LeftGroup");
            leftGO.transform.SetParent(root, false);
            var hlg = leftGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(10, 0, 0, 0);
            hlg.spacing = 6;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            AnchorLeft(leftGO.GetComponent<RectTransform>(), 340);

            // Connection indicator dot
            var dotGO = new GameObject("Dot");
            dotGO.transform.SetParent(leftGO.transform, false);
            connectionDot = dotGO.AddComponent<Image>();
            connectionDot.color = OfflineRed;
            connectionDot.raycastTarget = false;
            var dotLE = dotGO.AddComponent<LayoutElement>();
            dotLE.preferredWidth = 10;
            dotLE.preferredHeight = 10;

            meshStatusText = CreateText(leftGO.transform, "MeshStatus", "MESH: ---", 13, LabelColor);

            // ── Center: D-SPACE state ──────────────────────────────
            dspaceStateText = CreateText(root, "DSpaceState", "D-SPACE OFFLINE", 14, DSpaceCyan);
            var stateRT = dspaceStateText.rectTransform;
            stateRT.anchorMin = new Vector2(0.3f, 0);
            stateRT.anchorMax = new Vector2(0.7f, 1);
            stateRT.offsetMin = Vector2.zero;
            stateRT.offsetMax = Vector2.zero;
            dspaceStateText.alignment = TextAlignmentOptions.Center;
            dspaceStateText.fontStyle = FontStyles.Bold;

            // ── Right: GPS + Battery ───────────────────────────────
            var rightGO = new GameObject("RightGroup");
            rightGO.transform.SetParent(root, false);
            var rhlg = rightGO.AddComponent<HorizontalLayoutGroup>();
            rhlg.childAlignment = TextAnchor.MiddleRight;
            rhlg.padding = new RectOffset(0, 12, 0, 0);
            rhlg.spacing = 16;
            rhlg.childControlWidth = false;
            rhlg.childControlHeight = true;
            rhlg.childForceExpandWidth = false;
            rhlg.childForceExpandHeight = true;
            AnchorRight(rightGO.GetComponent<RectTransform>(), 360);

            gpsText     = CreateText(rightGO.transform, "GPS", "GPS: ---", 12, LabelColor);
            batteryText = CreateText(rightGO.transform, "Battery", "BAT: ---%", 12, LabelColor);
        }

        // ── Refresh ────────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            updateTimer += deltaTime;
            if (updateTimer < UpdateInterval) return;
            updateTimer = 0f;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            // Mesh status
            bool meshActive = meshNetwork?.IsActive ?? false;
            int peers = meshNetwork?.ConnectedPeerCount ?? 0;

            connectionDot.color = meshActive ? OnlineGreen : OfflineRed;
            meshStatusText.text = meshActive
                ? $"MESH: {peers} peer{(peers == 1 ? "" : "s")}"
                : "MESH: Offline";

            // D-Space state
            var state = DSpaceManager.Instance?.State ?? DSpaceState.Offline;
            switch (state)
            {
                case DSpaceState.Online:
                    dspaceStateText.text = "D-SPACE ONLINE";
                    dspaceStateText.color = DSpaceCyan;
                    break;
                case DSpaceState.Booting:
                    dspaceStateText.text = "D-SPACE BOOTING...";
                    dspaceStateText.color = new Color(1f, 0.85f, 0f, 1f);
                    break;
                case DSpaceState.Error:
                    dspaceStateText.text = "D-SPACE ERROR";
                    dspaceStateText.color = OfflineRed;
                    break;
                default:
                    dspaceStateText.text = "D-SPACE OFFLINE";
                    dspaceStateText.color = LabelColor;
                    break;
            }

            // GPS
            if (gpsProvider != null && gpsProvider.HasFix)
            {
                float acc = gpsProvider.Accuracy;
                gpsText.text = $"GPS: \u00B1{acc:F1}m";
                gpsText.color = acc < 5f ? OnlineGreen : (acc < 15f ? LabelColor : OfflineRed);
            }
            else
            {
                gpsText.text = "GPS: No fix";
                gpsText.color = OfflineRed;
            }

            // Battery
            float battery = SystemInfo.batteryLevel;
            if (battery >= 0f)
            {
                int pct = Mathf.RoundToInt(battery * 100f);
                batteryText.text  = $"BAT: {pct}%";
                batteryText.color = pct > 20 ? LabelColor : OfflineRed;
            }
            else
            {
                batteryText.text = "BAT: N/A";
            }
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void Show()  { IsVisible = true;  gameObject.SetActive(true); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

        // ── Helpers ────────────────────────────────────────────────

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void AnchorLeft(RectTransform rt, float width)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f); rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void AnchorRight(RectTransform rt, float width)
        {
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f); rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = Vector2.zero;
        }

        private static Image AddImage(RectTransform parent, string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name,
            string text, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.enableAutoSizing = false; tmp.raycastTarget = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 160;
            return tmp;
        }
    }
}
