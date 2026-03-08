// ClassSelectionUI.cs — Full-screen class selection overlay for the "Choose Your Path" quest
// 7 class cards in a grid: Fighter, Sorcerer, Shaman, Scout, Fabricator, Journalist, Rogue.
// Each card has name, colour accent, description, first 3 abilities, and a Select button.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;
using DaemonVision.Identity;
using DaemonVision.Social;

namespace DaemonVision.UI
{
    /// <summary>
    /// Full-screen class selection overlay presented during the "Choose Your Path" quest.
    /// 7 cards arranged in a 4+3 grid, each with accent-coloured header, description,
    /// first three abilities, and a Select button. Gaze/hover highlight on cards.
    /// </summary>
    public class ClassSelectionUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; }

        // ── Tuning ─────────────────────────────────────────────────
        private const float CardWidth   = 230f;
        private const float CardHeight  = 290f;
        private const float CardSpacing = 14f;
        private const float FadeDuration = 0.4f;

        // ── Colours ────────────────────────────────────────────────
        private static readonly Color OverlayBg   = new Color(0.01f, 0.01f, 0.03f, 0.92f);
        private static readonly Color CardBg      = new Color(0.06f, 0.06f, 0.1f, 0.9f);
        private static readonly Color CardHover   = new Color(0.1f, 0.12f, 0.18f, 0.95f);
        private static readonly Color TitleColor  = new Color(0.95f, 0.95f, 1f, 1f);
        private static readonly Color DescColor   = new Color(0.7f, 0.75f, 0.8f, 0.9f);
        private static readonly Color AbilityColor = new Color(0.6f, 0.7f, 0.8f, 0.8f);
        private static readonly Color ButtonText  = new Color(0.02f, 0.02f, 0.05f, 1f);
        private static readonly Color HeaderColor = new Color(0.9f, 0.95f, 1f, 1f);

        // Class colour map — matches ClassSystem definitions
        private static readonly Dictionary<DarknetClass, Color> ClassColors
            = new Dictionary<DarknetClass, Color>
        {
            { DarknetClass.Fighter,    new Color(1f, 0.3f, 0.2f) },
            { DarknetClass.Sorcerer,   new Color(0.5f, 0.2f, 1f) },
            { DarknetClass.Shaman,     new Color(0.2f, 0.8f, 0.4f) },
            { DarknetClass.Scout,      new Color(0.3f, 0.7f, 1f) },
            { DarknetClass.Fabricator, new Color(1f, 0.7f, 0.1f) },
            { DarknetClass.Journalist, new Color(1f, 1f, 0.3f) },
            { DarknetClass.Rogue,      new Color(0.4f, 0.4f, 0.4f) },
        };

        // Class icon placeholders (Unicode glyphs)
        private static readonly Dictionary<DarknetClass, string> ClassIcons
            = new Dictionary<DarknetClass, string>
        {
            { DarknetClass.Fighter,    "\u2694" },  // crossed swords
            { DarknetClass.Sorcerer,   "\u2604" },  // comet / magic
            { DarknetClass.Shaman,     "\u2618" },  // shamrock
            { DarknetClass.Scout,      "\u2316" },  // position indicator
            { DarknetClass.Fabricator, "\u2692" },  // hammer and pick
            { DarknetClass.Journalist, "\u270E" },  // pencil
            { DarknetClass.Rogue,      "\u2620" },  // skull
        };

        // ── Runtime refs ───────────────────────────────────────────
        private CanvasGroup panelGroup;
        private RectTransform panelRoot;
        private ClassSystem classSystem;
        private readonly List<CardRef> cards = new List<CardRef>();

        public event Action<DarknetClass> OnClassSelected;

        // ── Lifecycle ──────────────────────────────────────────────

        private void Start()
        {
            BuildUI();
            gameObject.SetActive(false); // hidden until requested
            IsVisible = false;

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
            classSystem = DSpaceManager.Instance.GetSubsystem<ClassSystem>();
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
            PopulateCards();
        }

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            panelRoot = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            panelGroup = gameObject.GetComponent<CanvasGroup>();
            if (panelGroup == null) panelGroup = gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;

            // Full-screen background
            var bg = AddImage(panelRoot, "Overlay", OverlayBg);
            StretchFill(bg.rectTransform);

            // Title
            var title = CreateTMP(panelRoot, "Title", "CHOOSE YOUR PATH", 28, HeaderColor,
                TextAlignmentOptions.Center, FontStyles.Bold);
            var tRT = title.rectTransform;
            tRT.anchorMin = new Vector2(0.5f, 1);
            tRT.anchorMax = new Vector2(0.5f, 1);
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.sizeDelta = new Vector2(600, 50);
            tRT.anchoredPosition = new Vector2(0, -30);

            var subtitle = CreateTMP(panelRoot, "Subtitle",
                "Select your darknet class. This defines your abilities and role in D-Space.",
                13, DescColor, TextAlignmentOptions.Center, FontStyles.Italic);
            var sRT = subtitle.rectTransform;
            sRT.anchorMin = new Vector2(0.5f, 1);
            sRT.anchorMax = new Vector2(0.5f, 1);
            sRT.pivot = new Vector2(0.5f, 1);
            sRT.sizeDelta = new Vector2(700, 24);
            sRT.anchoredPosition = new Vector2(0, -80);

            // Card grid container
            var gridGO = new GameObject("Grid");
            gridGO.transform.SetParent(panelRoot, false);
            var gridRT = gridGO.AddComponent<RectTransform>();
            gridRT.anchorMin = new Vector2(0.5f, 0.5f);
            gridRT.anchorMax = new Vector2(0.5f, 0.5f);
            gridRT.sizeDelta = new Vector2(
                4 * CardWidth + 3 * CardSpacing,
                2 * CardHeight + CardSpacing);
            gridRT.anchoredPosition = new Vector2(0, -20);

            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardWidth, CardHeight);
            grid.spacing = new Vector2(CardSpacing, CardSpacing);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;

            // Create 7 card slots
            DarknetClass[] classes =
            {
                DarknetClass.Fighter, DarknetClass.Sorcerer, DarknetClass.Shaman,
                DarknetClass.Scout, DarknetClass.Fabricator, DarknetClass.Journalist,
                DarknetClass.Rogue
            };

            foreach (var dc in classes)
                cards.Add(CreateClassCard(gridGO.transform, dc));

            // Back button — bottom center
            var backGO = new GameObject("BackButton");
            backGO.transform.SetParent(panelRoot, false);
            var backImg = backGO.AddComponent<Image>();
            backImg.color = new Color(0.15f, 0.15f, 0.2f, 0.8f);
            var backBtn = backGO.AddComponent<Button>();
            backBtn.onClick.AddListener(Close);
            var backRT = backGO.GetComponent<RectTransform>();
            backRT.anchorMin = new Vector2(0.5f, 0);
            backRT.anchorMax = new Vector2(0.5f, 0);
            backRT.pivot = new Vector2(0.5f, 0);
            backRT.sizeDelta = new Vector2(140, 36);
            backRT.anchoredPosition = new Vector2(0, 20);

            var backLabel = CreateTMP(backGO.transform, "Label", "BACK", 14,
                new Color(0.8f, 0.8f, 0.85f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFill(backLabel.rectTransform);
        }

        private CardRef CreateClassCard(Transform parent, DarknetClass dc)
        {
            Color accent = ClassColors.TryGetValue(dc, out var ac) ? ac : Color.gray;
            string icon = ClassIcons.TryGetValue(dc, out var ic) ? ic : "?";

            var card = new CardRef { DarknetClass = dc };

            var go = new GameObject($"Card_{dc}");
            go.transform.SetParent(parent, false);
            card.Background = go.AddComponent<Image>();
            card.Background.color = CardBg;

            // Accent stripe at top
            var stripe = AddImage(go.GetComponent<RectTransform>(), "Stripe", accent);
            stripe.rectTransform.anchorMin = new Vector2(0, 1);
            stripe.rectTransform.anchorMax = new Vector2(1, 1);
            stripe.rectTransform.pivot = new Vector2(0.5f, 1);
            stripe.rectTransform.sizeDelta = new Vector2(0, 4);
            stripe.rectTransform.anchoredPosition = Vector2.zero;

            // Icon
            var iconTMP = CreateTMP(go.transform, "Icon", icon, 32, accent,
                TextAlignmentOptions.Center, FontStyles.Normal);
            var iconRT = iconTMP.rectTransform;
            iconRT.anchorMin = new Vector2(0.5f, 1);
            iconRT.anchorMax = new Vector2(0.5f, 1);
            iconRT.pivot = new Vector2(0.5f, 1);
            iconRT.sizeDelta = new Vector2(50, 44);
            iconRT.anchoredPosition = new Vector2(0, -12);

            // Class name
            card.TitleText = CreateTMP(go.transform, "Name", dc.ToString(), 16, accent,
                TextAlignmentOptions.Center, FontStyles.Bold);
            var nRT = card.TitleText.rectTransform;
            nRT.anchorMin = new Vector2(0, 1);
            nRT.anchorMax = new Vector2(1, 1);
            nRT.pivot = new Vector2(0.5f, 1);
            nRT.sizeDelta = new Vector2(0, 22);
            nRT.anchoredPosition = new Vector2(0, -58);

            // Description (placeholder — populated later)
            card.DescriptionText = CreateTMP(go.transform, "Desc", "", 10, DescColor,
                TextAlignmentOptions.TopLeft, FontStyles.Normal);
            card.DescriptionText.overflowMode = TextOverflowModes.Truncate;
            card.DescriptionText.maxVisibleLines = 3;
            var dRT = card.DescriptionText.rectTransform;
            dRT.anchorMin = new Vector2(0, 1);
            dRT.anchorMax = new Vector2(1, 1);
            dRT.pivot = new Vector2(0.5f, 1);
            dRT.sizeDelta = new Vector2(-16, 48);
            dRT.anchoredPosition = new Vector2(0, -84);

            // Abilities (3 slots)
            card.AbilityTexts = new List<TextMeshProUGUI>();
            for (int a = 0; a < 3; a++)
            {
                var aTMP = CreateTMP(go.transform, $"Ability{a}", "", 9, AbilityColor,
                    TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
                aTMP.overflowMode = TextOverflowModes.Ellipsis;
                var aRT = aTMP.rectTransform;
                aRT.anchorMin = new Vector2(0, 1);
                aRT.anchorMax = new Vector2(1, 1);
                aRT.pivot = new Vector2(0.5f, 1);
                aRT.sizeDelta = new Vector2(-16, 16);
                aRT.anchoredPosition = new Vector2(0, -140 - a * 18);
                card.AbilityTexts.Add(aTMP);
            }

            // Select button
            var btnGO = new GameObject("SelectBtn");
            btnGO.transform.SetParent(go.transform, false);
            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = accent;
            var btn = btnGO.AddComponent<Button>();
            card.SelectButton = btn;

            var btnRT = btnGO.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.5f, 0);
            btnRT.anchorMax = new Vector2(0.5f, 0);
            btnRT.pivot = new Vector2(0.5f, 0);
            btnRT.sizeDelta = new Vector2(120, 30);
            btnRT.anchoredPosition = new Vector2(0, 12);

            var btnLabel = CreateTMP(btnGO.transform, "Label", "SELECT", 13,
                ButtonText, TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFill(btnLabel.rectTransform);

            // Hover highlight — EventTrigger
            var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entryEnter = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            entryEnter.callback.AddListener(_ => card.Background.color = CardHover);
            trigger.triggers.Add(entryEnter);

            var entryExit = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            entryExit.callback.AddListener(_ => card.Background.color = CardBg);
            trigger.triggers.Add(entryExit);

            DarknetClass captured = dc;
            btn.onClick.AddListener(() => SelectClass(captured));

            return card;
        }

        // ── Populate ───────────────────────────────────────────────

        private void PopulateCards()
        {
            if (classSystem == null) return;

            foreach (var card in cards)
            {
                var def = classSystem.GetClassDefinition(card.DarknetClass);
                if (def == null) continue;

                card.TitleText.text = def.Name;
                card.DescriptionText.text = def.Description;

                for (int a = 0; a < card.AbilityTexts.Count && a < def.Abilities.Count; a++)
                {
                    var ab = def.Abilities[a];
                    card.AbilityTexts[a].text = $"\u2022 {ab.Name} (Lv.{ab.UnlockLevel})";
                }
            }
        }

        // ── Actions ────────────────────────────────────────────────

        private void SelectClass(DarknetClass dc)
        {
            bool success = classSystem?.ChooseClass(dc) ?? false;
            if (success)
            {
                Debug.Log($"[ClassSelection] Class selected: {dc}");
                OnClassSelected?.Invoke(dc);
                Close();
            }
            else
            {
                Debug.LogWarning($"[ClassSelection] Cannot select {dc}.");
            }
        }

        // ── Show / Hide ────────────────────────────────────────────

        public void Show()
        {
            gameObject.SetActive(true);
            IsVisible = true;
            PopulateCards();
            StopAllCoroutines();
            StartCoroutine(Fade(0f, 1f));
        }

        public void Hide()
        {
            IsVisible = false;
            StopAllCoroutines();
            StartCoroutine(FadeAndDeactivate());
        }

        public void Close() => Hide();

        private IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < FadeDuration)
            {
                t += Time.unscaledDeltaTime;
                panelGroup.alpha = Mathf.Lerp(from, to, t / FadeDuration);
                yield return null;
            }
            panelGroup.alpha = to;
            panelGroup.interactable = to > 0.5f;
            panelGroup.blocksRaycasts = to > 0.5f;
        }

        private IEnumerator FadeAndDeactivate()
        {
            yield return Fade(panelGroup.alpha, 0f);
            gameObject.SetActive(false);
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void UpdateHUD(float deltaTime) { /* static panel */ }

        // ── Helpers ────────────────────────────────────────────────

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(RectTransform parent, string n, Color c)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name,
            string text, float size, Color color, TextAlignmentOptions align,
            FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.alignment = align; tmp.fontStyle = style;
            tmp.enableAutoSizing = false; tmp.raycastTarget = false;
            return tmp;
        }

        // ── Inner types ────────────────────────────────────────────

        private class CardRef
        {
            public DarknetClass DarknetClass;
            public Image Background;
            public TextMeshProUGUI TitleText;
            public TextMeshProUGUI DescriptionText;
            public List<TextMeshProUGUI> AbilityTexts;
            public Button SelectButton;
        }
    }
}
