// MinimapUI.cs — Radar-style circular minimap in the bottom-left corner
// Shows player dot, operative blips by threat level, anchor blips by type,
// range rings (50 m, 100 m, 200 m), and a north indicator.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;

namespace DaemonVision.UI
{
    /// <summary>
    /// 150 x 150 px circular minimap in the bottom-left of the HUD.
    /// Reads blip data from MinimapRenderer every frame and renders
    /// them as coloured dots over a dark, masked circular background.
    /// </summary>
    public class MinimapUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        private const float MapSize        = 150f;
        private const float HalfMap        = MapSize * 0.5f;
        private const int   MaxBlips       = 40;
        private const int   RangeRingCount = 3;

        private static readonly float[] RangeMeters = { 50f, 100f, 200f };

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor          = new Color(0.02f, 0.03f, 0.06f, 0.7f);
        private static readonly Color RingColor        = new Color(0.25f, 0.35f, 0.45f, 0.35f);
        private static readonly Color RingLabelColor   = new Color(0.45f, 0.55f, 0.65f, 0.5f);
        private static readonly Color PlayerDotColor   = Color.white;
        private static readonly Color NorthColor       = new Color(1f, 0.3f, 0.3f, 0.9f);
        private static readonly Color BorderColor      = new Color(0.15f, 0.25f, 0.35f, 0.6f);

        // ── Runtime refs ───────────────────────────────────────────
        private RectTransform mapRoot;
        private Image backgroundCircle;
        private Image playerDot;
        private TextMeshProUGUI northLabel;

        private readonly List<Image> blipPool = new List<Image>();
        private readonly List<Image> ringImages = new List<Image>();
        private readonly List<TextMeshProUGUI> ringLabels = new List<TextMeshProUGUI>();

        private MinimapRenderer minimapRenderer;

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
            minimapRenderer = DSpaceManager.Instance.GetSubsystem<MinimapRenderer>();
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
        }

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            mapRoot = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            // Circular background
            backgroundCircle = CreateCircleImage(mapRoot, "Background", BgColor, MapSize);
            StretchFill(backgroundCircle.rectTransform);

            // Circular border
            var border = CreateCircleImage(mapRoot, "Border", BorderColor, MapSize);
            StretchFill(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.fillCenter = false;

            // Circular mask so blips are clipped
            var maskGO = new GameObject("Mask");
            maskGO.transform.SetParent(mapRoot, false);
            var maskImg = maskGO.AddComponent<Image>();
            MakeCircle(maskImg);
            maskImg.color = Color.white;
            maskImg.raycastTarget = false;
            StretchFill(maskGO.GetComponent<RectTransform>());
            maskGO.AddComponent<Mask>().showMaskGraphic = false;

            var blipContainer = new GameObject("Blips");
            blipContainer.transform.SetParent(maskGO.transform, false);
            var bcRT = blipContainer.AddComponent<RectTransform>();
            StretchFill(bcRT);

            // Range rings
            for (int i = 0; i < RangeRingCount; i++)
            {
                float frac = RangeMeters[i] / RangeMeters[RangeRingCount - 1];
                float ringDia = MapSize * frac;

                var ring = CreateCircleImage(bcRT, $"Ring{i}", RingColor, ringDia);
                ring.type = Image.Type.Sliced;
                ring.fillCenter = false;
                ring.rectTransform.anchoredPosition = Vector2.zero;
                ring.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ringImages.Add(ring);

                // Range label
                var lbl = CreateTMP(bcRT, $"RLbl{i}", $"{RangeMeters[i]}m", 8,
                    RingLabelColor, TextAlignmentOptions.Center);
                lbl.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                lbl.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                lbl.rectTransform.sizeDelta = new Vector2(30, 12);
                lbl.rectTransform.anchoredPosition = new Vector2(ringDia * 0.35f, ringDia * 0.35f);
                ringLabels.Add(lbl);
            }

            // Player dot — center
            var pDot = new GameObject("PlayerDot");
            pDot.transform.SetParent(bcRT, false);
            playerDot = pDot.AddComponent<Image>();
            MakeCircle(playerDot);
            playerDot.color = PlayerDotColor;
            playerDot.raycastTarget = false;
            var pdRT = pDot.GetComponent<RectTransform>();
            pdRT.anchorMin = new Vector2(0.5f, 0.5f);
            pdRT.anchorMax = new Vector2(0.5f, 0.5f);
            pdRT.sizeDelta = new Vector2(6, 6);
            pdRT.anchoredPosition = Vector2.zero;

            // North indicator
            northLabel = CreateTMP(mapRoot, "North", "N", 11, NorthColor, TextAlignmentOptions.Center);
            northLabel.fontStyle = FontStyles.Bold;
            northLabel.rectTransform.anchorMin = new Vector2(0.5f, 1);
            northLabel.rectTransform.anchorMax = new Vector2(0.5f, 1);
            northLabel.rectTransform.sizeDelta = new Vector2(16, 14);
            northLabel.rectTransform.anchoredPosition = new Vector2(0, 2);

            // Blip pool
            for (int i = 0; i < MaxBlips; i++)
            {
                var bGO = new GameObject($"Blip{i}");
                bGO.transform.SetParent(bcRT, false);
                var bImg = bGO.AddComponent<Image>();
                MakeCircle(bImg);
                bImg.raycastTarget = false;
                bImg.enabled = false;
                var bRT = bGO.GetComponent<RectTransform>();
                bRT.anchorMin = new Vector2(0.5f, 0.5f);
                bRT.anchorMax = new Vector2(0.5f, 0.5f);
                bRT.sizeDelta = new Vector2(6, 6);
                blipPool.Add(bImg);
            }
        }

        // ── Update ─────────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            if (minimapRenderer == null) return;

            var blips = minimapRenderer.GetBlips();
            int count = Mathf.Min(blips.Count, MaxBlips);

            for (int i = 0; i < count; i++)
            {
                var blip = blips[i];
                var icon = blipPool[i];
                icon.enabled = true;
                icon.color = blip.Color;
                icon.rectTransform.sizeDelta = new Vector2(blip.Size, blip.Size);

                // Clamp within circle
                Vector2 pos = blip.Position;
                if (pos.magnitude > HalfMap - 4f)
                    pos = pos.normalized * (HalfMap - 4f);
                icon.rectTransform.anchoredPosition = pos;
            }

            // Disable unused blips
            for (int i = count; i < MaxBlips; i++)
                blipPool[i].enabled = false;

            // Rotate north indicator when map rotates with player
            float heading = DSpaceManager.Instance?.ARCamera != null
                ? DSpaceManager.Instance.ARCamera.transform.eulerAngles.y : 0f;

            // Position north label at the edge of the circle in the north direction
            float rad = (-heading + 90f) * Mathf.Deg2Rad;
            float nx = Mathf.Cos(rad) * (HalfMap + 2f);
            float ny = Mathf.Sin(rad) * (HalfMap + 2f);
            northLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            northLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            northLabel.rectTransform.anchoredPosition = new Vector2(nx, ny);
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

        /// <summary>
        /// Creates a circular Image by applying a runtime-generated circle sprite.
        /// </summary>
        private static Image CreateCircleImage(RectTransform parent, string name,
            Color color, float diameter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            MakeCircle(img);
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            return img;
        }

        /// <summary>
        /// Generates a circle sprite at runtime (64 x 64 texture).
        /// </summary>
        private static Sprite cachedCircleSprite;
        private static void MakeCircle(Image img)
        {
            if (cachedCircleSprite == null)
            {
                int res = 64;
                var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
                float center = res * 0.5f;
                float radius = center - 1f;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float dx = x - center;
                        float dy = y - center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(radius - dist + 0.5f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                tex.Apply();
                cachedCircleSprite = Sprite.Create(tex,
                    new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            }
            img.sprite = cachedCircleSprite;
        }

        private static TextMeshProUGUI CreateTMP(RectTransform parent, string name,
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
    }
}
