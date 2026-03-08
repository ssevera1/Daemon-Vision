// QuestTrackerUI.cs — Active quest tracker on the right side of the HUD
// Shows up to 3 active quests with title, current objective, and a progress bar.
// Gold accent colour for quest elements; checkmarks on completed objectives.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;
using DaemonVision.Quest;

namespace DaemonVision.UI
{
    /// <summary>
    /// Right-side quest tracker — 280 px wide, stacks up to 3 active quests vertically.
    /// Subscribes to QuestManager events for live updates.
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        private const int   MaxShownQuests = 3;
        private const float PanelWidth     = 280f;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor         = new Color(0.02f, 0.02f, 0.05f, 0.55f);
        private static readonly Color GoldAccent      = new Color(1f, 0.85f, 0f, 1f);
        private static readonly Color TitleColor      = new Color(1f, 0.9f, 0.5f, 1f);
        private static readonly Color ObjectiveColor  = new Color(0.8f, 0.85f, 0.9f, 0.9f);
        private static readonly Color CompletedColor  = new Color(0.4f, 0.8f, 0.3f, 0.8f);
        private static readonly Color ProgressBg      = new Color(0.1f, 0.1f, 0.15f, 0.7f);
        private static readonly Color ProgressFill    = new Color(1f, 0.85f, 0f, 0.85f);
        private static readonly Color DividerColor    = new Color(0.3f, 0.35f, 0.4f, 0.3f);

        // ── Runtime refs ───────────────────────────────────────────
        private VerticalLayoutGroup layoutGroup;
        private QuestManager questManager;
        private readonly List<QuestSlot> questSlots = new List<QuestSlot>();

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
            UnsubscribeQuests();
        }

        private void OnDSpaceReady()
        {
            questManager = DSpaceManager.Instance.GetSubsystem<QuestManager>();
            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
            SubscribeQuests();
            RefreshAll();
        }

        private void SubscribeQuests()
        {
            if (questManager == null) return;
            questManager.OnQuestAccepted    += OnQuestChanged;
            questManager.OnQuestCompleted   += _ => RefreshAll();
            questManager.OnQuestAbandoned   += _ => RefreshAll();
            questManager.OnObjectiveUpdated += (_, __) => RefreshAll();
        }

        private void UnsubscribeQuests()
        {
            if (questManager == null) return;
            questManager.OnQuestAccepted    -= OnQuestChanged;
            questManager.OnQuestCompleted   -= _ => RefreshAll();
            questManager.OnQuestAbandoned   -= _ => RefreshAll();
            questManager.OnObjectiveUpdated -= (_, __) => RefreshAll();
        }

        private void OnQuestChanged(QuestData q) => RefreshAll();

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            // Background
            var bg = AddImage(root, "BG", BgColor);
            StretchFill(bg.rectTransform);

            // Header
            var header = CreateTMP(root, "Header", "\u2691 ACTIVE QUESTS", 12, GoldAccent,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            var hRT = header.rectTransform;
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(1, 1);
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.sizeDelta = new Vector2(0, 22);
            hRT.anchoredPosition = Vector2.zero;
            hRT.offsetMin = new Vector2(8, hRT.offsetMin.y);

            // Scrollable content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(root, false);
            var cRT = contentGO.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 0);
            cRT.anchorMax = new Vector2(1, 1);
            cRT.offsetMin = new Vector2(0, 0);
            cRT.offsetMax = new Vector2(0, -24);

            layoutGroup = contentGO.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.spacing = 6;
            layoutGroup.padding = new RectOffset(8, 8, 4, 4);
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            // Pre-create quest slots
            for (int i = 0; i < MaxShownQuests; i++)
            {
                var slot = CreateQuestSlot(contentGO.transform, i);
                slot.Root.SetActive(false);
                questSlots.Add(slot);
            }
        }

        private QuestSlot CreateQuestSlot(Transform parent, int index)
        {
            var slot = new QuestSlot();

            var go = new GameObject($"Quest{index}");
            go.transform.SetParent(parent, false);
            slot.Root = go;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = PanelWidth - 16;

            // Quest title
            slot.Title = CreateTMP(go.transform, "Title", "", 13, TitleColor,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            slot.Title.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

            // Objectives container (up to 4)
            slot.ObjectiveTexts = new List<TextMeshProUGUI>();
            for (int o = 0; o < 4; o++)
            {
                var obj = CreateTMP(go.transform, $"Obj{o}", "", 11, ObjectiveColor,
                    TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
                obj.richText = true;
                obj.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;
                obj.gameObject.SetActive(false);
                slot.ObjectiveTexts.Add(obj);
            }

            // Progress bar
            var barGO = new GameObject("ProgressBar");
            barGO.transform.SetParent(go.transform, false);
            barGO.AddComponent<LayoutElement>().preferredHeight = 6;

            var barBg = barGO.AddComponent<Image>();
            barBg.color = ProgressBg;
            barBg.raycastTarget = false;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            slot.ProgressFill = fillGO.AddComponent<Image>();
            slot.ProgressFill.color = ProgressFill;
            slot.ProgressFill.raycastTarget = false;
            var fRT = fillGO.GetComponent<RectTransform>();
            fRT.anchorMin = Vector2.zero;
            fRT.anchorMax = new Vector2(0, 1);
            fRT.offsetMin = Vector2.zero;
            fRT.offsetMax = Vector2.zero;

            // Divider
            var div = new GameObject("Divider");
            div.transform.SetParent(go.transform, false);
            div.AddComponent<LayoutElement>().preferredHeight = 1;
            var divImg = div.AddComponent<Image>();
            divImg.color = DividerColor;
            divImg.raycastTarget = false;

            return slot;
        }

        // ── Refresh ────────────────────────────────────────────────

        private void RefreshAll()
        {
            if (questManager == null) return;

            var active = questManager.GetActiveQuests().Take(MaxShownQuests).ToList();

            for (int i = 0; i < MaxShownQuests; i++)
            {
                if (i < active.Count)
                    PopulateSlot(questSlots[i], active[i]);
                else
                    questSlots[i].Root.SetActive(false);
            }
        }

        private void PopulateSlot(QuestSlot slot, QuestData quest)
        {
            slot.Root.SetActive(true);
            slot.Title.text = quest.Title;

            // Objectives
            int totalProgress = 0;
            int totalRequired = 0;

            for (int o = 0; o < slot.ObjectiveTexts.Count; o++)
            {
                if (o < quest.Objectives.Count)
                {
                    var obj = quest.Objectives[o];
                    slot.ObjectiveTexts[o].gameObject.SetActive(true);

                    string check = obj.IsComplete
                        ? $"<color=#{ColorUtility.ToHtmlStringRGB(CompletedColor)}>\u2713</color> "
                        : "  \u25CB ";

                    string progress = obj.RequiredProgress > 1
                        ? $" ({obj.CurrentProgress}/{obj.RequiredProgress})"
                        : "";

                    slot.ObjectiveTexts[o].text = $"{check}{obj.Description}{progress}";
                    slot.ObjectiveTexts[o].color = obj.IsComplete ? CompletedColor : ObjectiveColor;

                    totalProgress += obj.CurrentProgress;
                    totalRequired += obj.RequiredProgress;
                }
                else
                {
                    slot.ObjectiveTexts[o].gameObject.SetActive(false);
                }
            }

            // Progress bar fill
            float pct = totalRequired > 0 ? (float)totalProgress / totalRequired : 0f;
            var fillRT = slot.ProgressFill.rectTransform;
            fillRT.anchorMax = new Vector2(Mathf.Clamp01(pct), 1);
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void UpdateHUD(float deltaTime) { /* event-driven updates */ }
        public void Show()  { IsVisible = true;  gameObject.SetActive(true); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

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
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        // ── Inner types ────────────────────────────────────────────

        private class QuestSlot
        {
            public GameObject Root;
            public TextMeshProUGUI Title;
            public List<TextMeshProUGUI> ObjectiveTexts;
            public Image ProgressFill;
        }
    }
}
