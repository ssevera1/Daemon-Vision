// ReticleUI.cs — Minimal centre reticle / crosshair with gaze-dwell indicator
// Subtle centre dot + ring, circular fill for gaze dwell progress,
// colour shift (cyan -> green) on interactable, pulse on selection.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;

namespace DaemonVision.UI
{
    /// <summary>
    /// 40 x 40 px centre reticle with:
    ///   - A small centre dot (3 px)
    ///   - An outer ring (40 px, thin)
    ///   - A circular-fill progress image for gaze dwell
    ///   - Colour transition cyan -> green when aiming at an interactable
    ///   - A brief pulse animation on confirmed selection
    /// </summary>
    public class ReticleUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        [Header("Reticle")]
        [SerializeField] private float reticleSize   = 40f;
        [SerializeField] private float dotSize       = 3f;
        [SerializeField] private float ringThickness = 1.5f;

        [Header("Gaze Dwell")]
        [SerializeField] private float dwellTime = 1.5f;

        [Header("Pulse")]
        [SerializeField] private float pulseScale    = 1.6f;
        [SerializeField] private float pulseDuration = 0.3f;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color IdleCyan        = new Color(0f, 0.85f, 1f, 0.45f);
        private static readonly Color InteractGreen   = new Color(0.2f, 1f, 0.4f, 0.7f);
        private static readonly Color DwellFillColor  = new Color(0f, 1f, 0.6f, 0.55f);
        private static readonly Color DotColor        = new Color(1f, 1f, 1f, 0.6f);

        // ── Runtime refs ───────────────────────────────────────────
        private RectTransform rootRT;
        private Image centerDot;
        private Image outerRing;
        private Image dwellFill;
        private CanvasGroup reticleGroup;

        // Gaze state
        private bool isGazingAtInteractable;
        private float dwellProgress; // 0..1
        private bool isPulsing;

        // Cached circle sprite
        private static Sprite circleSprite;
        private static Sprite ringSprite;

        // ── Lifecycle ──────────────────────────────────────────────

        private void Start()
        {
            GenerateSprites();
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
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);

            // Read dwell time from config if available
            var cfg = DSpaceManager.Instance.Config;
            if (cfg != null) dwellTime = cfg.GazeDwellTime;
        }

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            rootRT = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            reticleGroup = gameObject.GetComponent<CanvasGroup>();
            if (reticleGroup == null) reticleGroup = gameObject.AddComponent<CanvasGroup>();

            // Dwell fill — circular fill behind the ring
            dwellFill = CreateImageChild("DwellFill", circleSprite, DwellFillColor, reticleSize);
            dwellFill.type = Image.Type.Filled;
            dwellFill.fillMethod = Image.FillMethod.Radial360;
            dwellFill.fillOrigin = (int)Image.Origin360.Top;
            dwellFill.fillClockwise = true;
            dwellFill.fillAmount = 0f;

            // Outer ring
            outerRing = CreateImageChild("Ring", ringSprite, IdleCyan, reticleSize);

            // Center dot
            centerDot = CreateImageChild("Dot", circleSprite, DotColor, dotSize);
        }

        // ── Public API ─────────────────────────────────────────────

        /// <summary>Call each frame from the gaze/input system.</summary>
        public void SetGazeState(bool onInteractable, float progress01)
        {
            isGazingAtInteractable = onInteractable;
            dwellProgress = Mathf.Clamp01(progress01);
        }

        /// <summary>Trigger a selection pulse (e.g., dwell completed).</summary>
        public void TriggerSelectionPulse()
        {
            if (!isPulsing) StartCoroutine(PulseAnimation());
        }

        // ── Update ─────────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            // Smooth colour transition
            Color targetColor = isGazingAtInteractable ? InteractGreen : IdleCyan;
            outerRing.color = Color.Lerp(outerRing.color, targetColor, deltaTime * 8f);
            dwellFill.color = Color.Lerp(dwellFill.color,
                isGazingAtInteractable ? DwellFillColor : new Color(DwellFillColor.r, DwellFillColor.g, DwellFillColor.b, 0f),
                deltaTime * 8f);

            // Fill amount
            dwellFill.fillAmount = dwellProgress;

            // Subtle breathing on the dot
            float breath = 1f + Mathf.Sin(Time.time * 2f) * 0.08f;
            centerDot.rectTransform.localScale = Vector3.one * breath;
        }

        // ── Pulse ──────────────────────────────────────────────────

        private IEnumerator PulseAnimation()
        {
            isPulsing = true;
            float halfDur = pulseDuration * 0.5f;

            // Scale up
            float t = 0f;
            while (t < halfDur)
            {
                t += Time.unscaledDeltaTime;
                float frac = t / halfDur;
                float s = Mathf.Lerp(1f, pulseScale, EaseOutQuad(frac));
                rootRT.localScale = Vector3.one * s;
                reticleGroup.alpha = Mathf.Lerp(1f, 0.6f, frac);
                yield return null;
            }

            // Scale down
            t = 0f;
            while (t < halfDur)
            {
                t += Time.unscaledDeltaTime;
                float frac = t / halfDur;
                float s = Mathf.Lerp(pulseScale, 1f, EaseInQuad(frac));
                rootRT.localScale = Vector3.one * s;
                reticleGroup.alpha = Mathf.Lerp(0.6f, 1f, frac);
                yield return null;
            }

            rootRT.localScale = Vector3.one;
            reticleGroup.alpha = 1f;
            isPulsing = false;
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void Show()  { IsVisible = true;  gameObject.SetActive(true); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

        // ── Sprite Generation ──────────────────────────────────────

        private static void GenerateSprites()
        {
            if (circleSprite != null) return;

            int res = 64;
            float center = res * 0.5f;

            // Solid circle
            {
                var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
                float rad = center - 1f;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float a = Mathf.Clamp01(rad - d + 0.5f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                tex.Apply();
                circleSprite = Sprite.Create(tex,
                    new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            }

            // Ring (hollow circle)
            {
                var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
                float outerR = center - 1f;
                float innerR = outerR - 2.5f;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float outerA = Mathf.Clamp01(outerR - d + 0.5f);
                        float innerA = Mathf.Clamp01(d - innerR + 0.5f);
                        float a = Mathf.Min(outerA, innerA);
                        tex.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                tex.Apply();
                ringSprite = Sprite.Create(tex,
                    new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // ── Helpers ────────────────────────────────────────────────

        private Image CreateImageChild(string name, Sprite sprite, Color color, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rootRT, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        private static float EaseOutQuad(float t) { return t * (2f - t); }
        private static float EaseInQuad(float t)  { return t * t; }
    }
}
