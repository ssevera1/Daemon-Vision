// CompassStripUI.cs — Horizontal compass strip with cardinal directions,
// heading readout, quest-objective diamonds, and operative dots.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;

namespace DaemonVision.UI
{
    /// <summary>
    /// Renders a 600 x 30 px compass strip centered at the top, just below the
    /// top status bar.  Tick marks every 15 degrees, cardinal labels, coloured
    /// quest-diamond and operative-dot markers sourced from CompassOverlay.
    /// </summary>
    public class CompassStripUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        private const float StripWidth  = 600f;
        private const float StripHeight = 30f;
        private const float FieldOfView = 180f; // degrees visible

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor      = new Color(0.02f, 0.02f, 0.05f, 0.55f);
        private static readonly Color TickColor    = new Color(0.5f, 0.6f, 0.7f, 0.6f);
        private static readonly Color CardinalColor = new Color(0.9f, 0.95f, 1f, 0.9f);
        private static readonly Color HeadingColor = new Color(0f, 0.85f, 1f, 1f);
        private static readonly Color NorthColor   = new Color(1f, 0.3f, 0.3f, 1f);

        // ── Runtime refs ───────────────────────────────────────────
        private RectTransform stripRoot;
        private RectTransform tickContainer;
        private RectTransform markerContainer;
        private TextMeshProUGUI headingText;

        private CompassOverlay compassOverlay;

        // Object pools for tick labels / markers
        private readonly List<TextMeshProUGUI> tickLabels = new List<TextMeshProUGUI>();
        private readonly List<RectTransform> tickMarks   = new List<RectTransform>();
        private readonly List<Image> markerIcons         = new List<Image>();

        private static readonly string[] Cardinals =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private static readonly float[] CardinalAngles =
            { 0, 45, 90, 135, 180, 225, 270, 315 };

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
            compassOverlay = DSpaceManager.Instance.GetSubsystem<CompassOverlay>();
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
        }

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            stripRoot = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            // Background
            var bg = new GameObject("BG");
            bg.transform.SetParent(stripRoot, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = BgColor;
            bgImg.raycastTarget = false;
            StretchFill(bg.GetComponent<RectTransform>());

            // Masked tick container (clips ticks outside the strip)
            var maskGO = new GameObject("Mask");
            maskGO.transform.SetParent(stripRoot, false);
            var maskImg = maskGO.AddComponent<Image>();
            maskImg.color = new Color(0, 0, 0, 0); // invisible
            maskGO.AddComponent<Mask>().showMaskGraphic = false;
            StretchFill(maskGO.GetComponent<RectTransform>());

            tickContainer = new GameObject("Ticks").AddComponent<RectTransform>().GetComponent<RectTransform>();
            tickContainer.SetParent(maskGO.transform, false);
            tickContainer.sizeDelta = new Vector2(StripWidth * 2f, StripHeight);

            markerContainer = new GameObject("Markers").AddComponent<RectTransform>().GetComponent<RectTransform>();
            markerContainer.SetParent(maskGO.transform, false);
            markerContainer.sizeDelta = new Vector2(StripWidth, StripHeight);

            // Pre-create tick marks: every 15 degrees across 360
            for (int deg = 0; deg < 360; deg += 15)
            {
                bool isCardinal = deg % 45 == 0;
                float tickH = isCardinal ? 12f : 6f;

                // Tick line
                var tick = CreateRect(tickContainer, $"T{deg}", 1.2f, tickH, TickColor);
                tickMarks.Add(tick);

                // Label for cardinals
                if (isCardinal)
                {
                    int idx = deg / 45;
                    string label = Cardinals[idx];
                    Color col = label == "N" ? NorthColor : CardinalColor;
                    var txt = CreateTMP(tickContainer, $"L{deg}", label, 11,
                        FontStyles.Bold, col, TextAlignmentOptions.Center);
                    txt.rectTransform.sizeDelta = new Vector2(30, 14);
                    tickLabels.Add(txt);
                }
            }

            // Heading readout — small text centered above the strip
            headingText = CreateTMP(stripRoot, "Heading", "000\u00B0", 11,
                FontStyles.Bold, HeadingColor, TextAlignmentOptions.Center);
            headingText.rectTransform.anchoredPosition = new Vector2(0, -StripHeight - 8);
            headingText.rectTransform.sizeDelta = new Vector2(50, 16);

            // Center indicator tick (white line in the center)
            var center = CreateRect(stripRoot, "CenterTick", 2f, StripHeight, HeadingColor);
            center.anchoredPosition = Vector2.zero;

            // Pre-pool marker icons
            for (int i = 0; i < 16; i++)
            {
                var mImg = new GameObject($"Marker{i}");
                mImg.transform.SetParent(markerContainer, false);
                var img = mImg.AddComponent<Image>();
                img.raycastTarget = false;
                img.enabled = false;
                var rt = mImg.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(8, 8);
                markerIcons.Add(img);
            }
        }

        // ── Update ─────────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            float heading = compassOverlay?.GetHeading() ?? 0f;
            headingText.text = $"{Mathf.RoundToInt(heading):000}\u00B0";

            // Position tick marks
            int tickIdx = 0;
            int labelIdx = 0;
            for (int deg = 0; deg < 360; deg += 15)
            {
                float relAngle = Mathf.DeltaAngle(heading, deg);
                float x = (relAngle / FieldOfView) * StripWidth;
                bool visible = Mathf.Abs(relAngle) <= FieldOfView * 0.5f;

                if (tickIdx < tickMarks.Count)
                {
                    tickMarks[tickIdx].anchoredPosition = new Vector2(x, 0);
                    tickMarks[tickIdx].gameObject.SetActive(visible);
                }
                tickIdx++;

                if (deg % 45 == 0 && labelIdx < tickLabels.Count)
                {
                    tickLabels[labelIdx].rectTransform.anchoredPosition = new Vector2(x, -10);
                    tickLabels[labelIdx].gameObject.SetActive(visible);
                    labelIdx++;
                }
            }

            // Position markers from CompassOverlay
            var markers = compassOverlay?.GetMarkers();
            int mi = 0;
            if (markers != null)
            {
                foreach (var marker in markers)
                {
                    if (mi >= markerIcons.Count) break;
                    var icon = markerIcons[mi];
                    if (marker.IsVisible)
                    {
                        icon.enabled = true;
                        icon.color = marker.Color;
                        icon.rectTransform.anchoredPosition = new Vector2(marker.CompassX, 2);

                        // Quest markers as diamonds (rotated 45), operatives as dots
                        bool isDiamond = marker.Type == CompassMarkerType.QuestObjective;
                        icon.rectTransform.localRotation =
                            isDiamond ? Quaternion.Euler(0, 0, 45) : Quaternion.identity;
                        icon.rectTransform.sizeDelta = isDiamond
                            ? new Vector2(8, 8) : new Vector2(6, 6);
                    }
                    else
                    {
                        icon.enabled = false;
                    }
                    mi++;
                }
            }

            // Hide unused marker icons
            for (; mi < markerIcons.Count; mi++)
                markerIcons[mi].enabled = false;
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

        private static RectTransform CreateRect(RectTransform parent, string name,
            float w, float h, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static TextMeshProUGUI CreateTMP(RectTransform parent, string name,
            string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.color = color; tmp.alignment = align;
            tmp.enableAutoSizing = false; tmp.raycastTarget = false;
            return tmp;
        }
    }
}
