// TopStatusBarUI.cs — Top status bar showing operative identity and stats
// Daemon-style HUD: Callsign (bold cyan), level badge, class icon on the left;
// credit count with diamond symbol, active quest count with flag on the right.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;
using DaemonVision.Identity;
using DaemonVision.Economy;
using DaemonVision.Quest;

namespace DaemonVision.UI
{
    /// <summary>
    /// Renders the top status bar with operative identity on the left and
    /// economy/quest counts on the right. Updates every 0.5 s from the
    /// StatusBarRenderer subsystem.
    /// </summary>
    public class TopStatusBarUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color CyanCallsign  = new Color(0f, 0.85f, 1f, 1f);
        private static readonly Color LevelBadge    = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color ClassColor    = new Color(0.75f, 0.85f, 1f, 1f);
        private static readonly Color CreditColor   = new Color(0f, 1f, 0.65f, 1f);
        private static readonly Color QuestColor    = new Color(1f, 0.85f, 0f, 1f);
        private static readonly Color BgColor       = new Color(0.02f, 0.02f, 0.05f, 0.7f);

        // ── Runtime refs ───────────────────────────────────────────
        private RectTransform root;
        private Image background;
        private TextMeshProUGUI callsignText;
        private TextMeshProUGUI levelText;
        private TextMeshProUGUI classText;
        private TextMeshProUGUI creditsText;
        private TextMeshProUGUI questCountText;

        // Data sources
        private StatusBarRenderer statusRenderer;
        private DarknetIdentityManager identityManager;
        private DarknetEconomy economy;
        private QuestManager questManager;

        private float updateTimer;
        private const float UpdateInterval = 0.5f;

        // ── Setup ──────────────────────────────────────────────────

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
            statusRenderer  = DSpaceManager.Instance.GetSubsystem<StatusBarRenderer>();
            identityManager = DSpaceManager.Instance.GetSubsystem<DarknetIdentityManager>();
            economy         = DSpaceManager.Instance.GetSubsystem<DarknetEconomy>();
            questManager    = DSpaceManager.Instance.GetSubsystem<QuestManager>();

            var hudMgr = DSpaceManager.Instance.GetSubsystem<HUDManager>();
            hudMgr?.RegisterHUDElement(this);

            RefreshDisplay();
        }

        // ── UI Construction ────────────────────────────────────────

        private void BuildUI()
        {
            // Assume this component sits on the TopStatusBar zone created by DSpaceUIManager
            root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();

            // Stretch to fill parent
            StretchFill(root);

            // Background
            background = CreateImage(root, "BG", BgColor);
            StretchFill(background.rectTransform);

            // ── Left group: Callsign | Level | Class ───────────────
            var leftGroup = CreateHorizontalGroup("LeftGroup", root,
                TextAnchor.MiddleLeft, new RectOffset(12, 0, 0, 0), 8);
            AnchorLeft(leftGroup.GetComponent<RectTransform>(), 600, 50);

            callsignText = CreateTMPText(leftGroup.transform, "Callsign", "---",
                18, FontStyles.Bold, CyanCallsign, TextAlignmentOptions.MidlineLeft);

            levelText = CreateTMPText(leftGroup.transform, "Level", "Lv.1",
                14, FontStyles.Normal, LevelBadge, TextAlignmentOptions.MidlineLeft);

            classText = CreateTMPText(leftGroup.transform, "Class", "",
                14, FontStyles.Italic, ClassColor, TextAlignmentOptions.MidlineLeft);

            // ── Right group: Credits | Quests ──────────────────────
            var rightGroup = CreateHorizontalGroup("RightGroup", root,
                TextAnchor.MiddleRight, new RectOffset(0, 12, 0, 0), 14);
            AnchorRight(rightGroup.GetComponent<RectTransform>(), 400, 50);

            creditsText = CreateTMPText(rightGroup.transform, "Credits", "\u25C8 0",
                15, FontStyles.Normal, CreditColor, TextAlignmentOptions.MidlineRight);

            questCountText = CreateTMPText(rightGroup.transform, "Quests", "\u2691 0 quests",
                14, FontStyles.Normal, QuestColor, TextAlignmentOptions.MidlineRight);
        }

        // ── Update Loop ────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            updateTimer += deltaTime;
            if (updateTimer < UpdateInterval) return;
            updateTimer = 0f;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            // Prefer live data; fall back to StatusBarRenderer cached strings
            var identity = identityManager?.LocalIdentity;

            if (identity != null)
            {
                callsignText.text = identity.Callsign;
                levelText.text    = $"Lv.{identity.Level}";
                classText.text    = identity.DarknetClass == DarknetClass.Unassigned
                    ? "" : identity.DarknetClass.ToString();

                long credits     = economy?.GetBalance() ?? 0;
                int activeQuests = questManager?.ActiveQuestCount ?? 0;

                creditsText.text    = $"\u25C8 {credits:N0}";
                questCountText.text = $"\u2691 {activeQuests} quest{(activeQuests == 1 ? "" : "s")}";
            }
            else if (statusRenderer != null)
            {
                callsignText.text   = statusRenderer.CallsignLine;
                creditsText.text    = statusRenderer.StatsLine;
                levelText.text      = "";
                classText.text      = "";
                questCountText.text = "";
            }
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void Show()  { IsVisible = true; gameObject.SetActive(true); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

        // ── Helpers ────────────────────────────────────────────────

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AnchorLeft(RectTransform rt, float width, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void AnchorRight(RectTransform rt, float width, float height)
        {
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = Vector2.zero;
        }

        private static Image CreateImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static GameObject CreateHorizontalGroup(string name, RectTransform parent,
            TextAnchor alignment, RectOffset padding, float spacing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = alignment;
            hlg.padding = padding;
            hlg.spacing = spacing;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            return go;
        }

        private static TextMeshProUGUI CreateTMPText(Transform parent, string name, string text,
            float fontSize, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = tmp.preferredWidth > 0 ? tmp.preferredWidth + 10 : 120;
            le.flexibleWidth = 0;

            return tmp;
        }
    }
}
