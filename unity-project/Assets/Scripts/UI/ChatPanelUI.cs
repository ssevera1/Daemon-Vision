// ChatPanelUI.cs — Chat overlay panel for D-Space darknet communication
// Semi-transparent bottom-right panel showing the last 8 messages,
// channel tabs (Local/Global/Faction/System), auto-hide after 10 s.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.HUD;
using DaemonVision.Communication;
using DaemonVision.Social;

namespace DaemonVision.UI
{
    /// <summary>
    /// Chat overlay — 350 x 250 px, bottom-right.
    /// Subscribes to ChatSystem.OnMessageReceived, displays last 8 messages
    /// with callsign-coloured prefixes and timestamps.
    /// Auto-hides after 10 s of inactivity; reappears on new message.
    /// </summary>
    public class ChatPanelUI : MonoBehaviour, IHUDElement
    {
        // ── State ──────────────────────────────────────────────────
        public bool IsVisible { get; private set; } = true;

        // ── Tuning ─────────────────────────────────────────────────
        private const int   MaxVisibleMessages = 8;
        private const float AutoHideDelay      = 10f;
        private const float FadeDuration       = 0.4f;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor         = new Color(0.02f, 0.02f, 0.05f, 0.6f);
        private static readonly Color InputBgColor    = new Color(0.05f, 0.05f, 0.1f, 0.7f);
        private static readonly Color TabActiveColor  = new Color(0f, 0.75f, 1f, 1f);
        private static readonly Color TabInactiveColor = new Color(0.5f, 0.55f, 0.6f, 0.7f);
        private static readonly Color TimestampColor  = new Color(0.45f, 0.5f, 0.55f, 0.8f);
        private static readonly Color MessageColor    = new Color(0.85f, 0.9f, 0.95f, 1f);
        private static readonly Color SystemColor     = new Color(1f, 0.85f, 0f, 0.9f);

        // ── Runtime refs ───────────────────────────────────────────
        private CanvasGroup panelGroup;
        private RectTransform messagesContainer;
        private TMP_InputField inputField;
        private readonly List<TextMeshProUGUI> messageSlots = new List<TextMeshProUGUI>();
        private readonly Dictionary<string, Button> tabButtons = new Dictionary<string, Button>();

        private ChatSystem chatSystem;
        private FactionManager factionManager;

        private float hideTimer;
        private bool isFadedOut;
        private string activeChannel = "local";
        private readonly string[] channelIds = { "local", "global", "faction", "system" };
        private readonly string[] channelLabels = { "Local", "Global", "Faction", "System" };

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
            if (chatSystem != null)
                chatSystem.OnMessageReceived -= OnChatMessage;
        }

        private void OnDSpaceReady()
        {
            chatSystem     = DSpaceManager.Instance.GetSubsystem<ChatSystem>();
            factionManager = DSpaceManager.Instance.GetSubsystem<FactionManager>();

            if (chatSystem != null)
                chatSystem.OnMessageReceived += OnChatMessage;

            DSpaceManager.Instance.GetSubsystem<HUDManager>()?.RegisterHUDElement(this);
            RefreshMessages();
        }

        // ── Build ──────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            panelGroup = gameObject.GetComponent<CanvasGroup>();
            if (panelGroup == null) panelGroup = gameObject.AddComponent<CanvasGroup>();

            // Background
            var bg = AddImage(root, "BG", BgColor);
            StretchFill(bg.rectTransform);

            // ── Channel tabs across the top ────────────────────────
            var tabBar = new GameObject("Tabs");
            tabBar.transform.SetParent(root, false);
            var tabHLG = tabBar.AddComponent<HorizontalLayoutGroup>();
            tabHLG.spacing = 2;
            tabHLG.padding = new RectOffset(4, 4, 2, 0);
            tabHLG.childControlWidth = true;
            tabHLG.childControlHeight = true;
            tabHLG.childForceExpandWidth = true;
            tabHLG.childForceExpandHeight = false;
            var tabRT = tabBar.GetComponent<RectTransform>();
            tabRT.anchorMin = new Vector2(0, 1);
            tabRT.anchorMax = new Vector2(1, 1);
            tabRT.pivot = new Vector2(0.5f, 1);
            tabRT.sizeDelta = new Vector2(0, 24);
            tabRT.anchoredPosition = Vector2.zero;

            for (int i = 0; i < channelIds.Length; i++)
            {
                string chId = channelIds[i];
                var btn = CreateTabButton(tabBar.transform, channelLabels[i], chId);
                tabButtons[chId] = btn;
            }

            // ── Messages area ──────────────────────────────────────
            var msgArea = new GameObject("Messages");
            msgArea.transform.SetParent(root, false);
            var msgRT = msgArea.AddComponent<RectTransform>();
            msgRT.anchorMin = new Vector2(0, 0);
            msgRT.anchorMax = new Vector2(1, 1);
            msgRT.offsetMin = new Vector2(6, 32);   // above input
            msgRT.offsetMax = new Vector2(-6, -26);  // below tabs

            var vlg = msgArea.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.LowerLeft;
            vlg.spacing = 2;
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = msgArea.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            messagesContainer = msgRT;

            for (int i = 0; i < MaxVisibleMessages; i++)
            {
                var slot = CreateTMP(msgArea.transform, $"Msg{i}", "", 11, MessageColor);
                slot.richText = true;
                slot.overflowMode = TextOverflowModes.Truncate;
                var le = slot.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 16;
                slot.gameObject.SetActive(false);
                messageSlots.Add(slot);
            }

            // ── Input field at bottom ──────────────────────────────
            var inputGO = new GameObject("Input");
            inputGO.transform.SetParent(root, false);
            var inputBg = inputGO.AddComponent<Image>();
            inputBg.color = InputBgColor;
            var inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0, 0);
            inputRT.anchorMax = new Vector2(1, 0);
            inputRT.pivot = new Vector2(0.5f, 0);
            inputRT.sizeDelta = new Vector2(0, 28);
            inputRT.anchoredPosition = Vector2.zero;

            // Text area child required by TMP_InputField
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            textArea.AddComponent<RectMask2D>();
            var taRT = textArea.AddComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(8, 2); taRT.offsetMax = new Vector2(-8, -2);

            var placeholder = CreateTMP(textArea.transform, "Placeholder", "Type message...",
                12, new Color(0.5f, 0.55f, 0.6f, 0.5f));
            placeholder.fontStyle = FontStyles.Italic;

            var inputText = CreateTMP(textArea.transform, "Text", "", 12, MessageColor);

            inputField = inputGO.AddComponent<TMP_InputField>();
            inputField.textViewport = taRT;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.fontAsset = inputText.font;
            inputField.pointSize = 12;
            inputField.onSubmit.AddListener(OnInputSubmit);

            RefreshTabs();
        }

        // ── Event Handlers ─────────────────────────────────────────

        private void OnChatMessage(ChatMessage msg)
        {
            ResetAutoHide();
            if (isFadedOut) FadeIn();
            RefreshMessages();
        }

        private void OnInputSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            chatSystem?.SendMessage(activeChannel, text);
            inputField.text = "";
            inputField.ActivateInputField();
            ResetAutoHide();
        }

        private void SwitchChannel(string channelId)
        {
            activeChannel = channelId;
            chatSystem?.SwitchChannel(channelId);
            RefreshTabs();
            RefreshMessages();
            ResetAutoHide();
        }

        // ── Display ────────────────────────────────────────────────

        private void RefreshMessages()
        {
            var messages = chatSystem?.GetChannelMessages(activeChannel);
            if (messages == null) { ClearSlots(); return; }

            int startIdx = Mathf.Max(0, messages.Count - MaxVisibleMessages);
            int slot = 0;
            for (int i = startIdx; i < messages.Count && slot < MaxVisibleMessages; i++, slot++)
            {
                var msg = messages[i];
                var s = messageSlots[slot];
                s.gameObject.SetActive(true);

                string time = FormatTimestamp(msg.Timestamp);
                Color callsignColor = GetFactionColor(msg.SenderCallsign);
                string hex = ColorUtility.ToHtmlStringRGB(callsignColor);

                if (msg.ChannelId == "system")
                {
                    string sysHex = ColorUtility.ToHtmlStringRGB(SystemColor);
                    s.text = $"<color=#{sysHex}>[SYS]</color> {msg.Text}";
                }
                else
                {
                    s.text = $"<color=#{ColorUtility.ToHtmlStringRGB(TimestampColor)}>{time}</color> " +
                             $"<color=#{hex}>[{msg.SenderCallsign}]</color> {msg.Text}";
                }
            }

            for (; slot < MaxVisibleMessages; slot++)
                messageSlots[slot].gameObject.SetActive(false);
        }

        private void ClearSlots()
        {
            foreach (var s in messageSlots) s.gameObject.SetActive(false);
        }

        private void RefreshTabs()
        {
            foreach (var kvp in tabButtons)
            {
                var txt = kvp.Value.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                    txt.color = kvp.Key == activeChannel ? TabActiveColor : TabInactiveColor;
            }
        }

        // ── Auto-Hide ──────────────────────────────────────────────

        public void UpdateHUD(float deltaTime)
        {
            if (isFadedOut) return;
            hideTimer += deltaTime;
            if (hideTimer >= AutoHideDelay)
                FadeOut();
        }

        private void ResetAutoHide() { hideTimer = 0f; }

        private void FadeOut()
        {
            if (isFadedOut) return;
            isFadedOut = true;
            StartCoroutine(Fade(panelGroup, panelGroup.alpha, 0.15f, FadeDuration));
        }

        private void FadeIn()
        {
            isFadedOut = false;
            hideTimer = 0f;
            StartCoroutine(Fade(panelGroup, panelGroup.alpha, 1f, FadeDuration));
        }

        private IEnumerator Fade(CanvasGroup cg, float from, float to, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, t / dur);
                yield return null;
            }
            cg.alpha = to;
        }

        // ── IHUDElement ────────────────────────────────────────────

        public void Show()  { IsVisible = true; gameObject.SetActive(true); FadeIn(); }
        public void Hide()  { IsVisible = false; gameObject.SetActive(false); }

        // ── Helpers ────────────────────────────────────────────────

        private string FormatTimestamp(long unix)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            return dt.ToString("HH:mm");
        }

        private Color GetFactionColor(string callsign)
        {
            // Default cyan; in a full implementation, map via FactionManager
            return new Color(0f, 0.85f, 1f, 1f);
        }

        private Button CreateTabButton(Transform parent, string label, string channelId)
        {
            var go = new GameObject($"Tab_{channelId}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.12f, 0.5f);
            var btn = go.AddComponent<Button>();

            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = 22;

            var txt = CreateTMP(go.transform, "Label", label, 10, TabInactiveColor);
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            StretchFill(txt.rectTransform);

            string captured = channelId;
            btn.onClick.AddListener(() => SwitchChannel(captured));
            return btn;
        }

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
            string text, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color;
            tmp.enableAutoSizing = false; tmp.raycastTarget = false;
            return tmp;
        }
    }
}
