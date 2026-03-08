// ChatSystem.cs — D-Space chat overlay
// In the Daemon, operatives communicate through encrypted darknet channels.
// Messages appear as AR overlays — either floating text near the sender,
// or in a HUD chat panel. Supports public, faction, and private channels.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;
using DaemonVision.Network;

namespace DaemonVision.Communication
{
    public class ChatSystem : SubsystemBase
    {
        public override string Name => "Chat";

        [Header("Chat Settings")]
        [SerializeField] private int maxMessagesPerChannel = 200;
        [SerializeField] private float spatialChatRange = 30f;     // meters for local chat
        [SerializeField] private float messageDisplayDuration = 10f;
        [SerializeField] private bool showSpatialBubbles = true;    // Float messages near sender

        private DarknetIdentityManager identityManager;
        private MeshNetworkManager meshNetwork;

        private readonly Dictionary<string, ChatChannel> channels
            = new Dictionary<string, ChatChannel>();

        public string ActiveChannelId { get; private set; } = "local";

        public event Action<ChatMessage> OnMessageReceived;
        public event Action<string> OnChannelChanged;

        protected override Task OnInitialize()
        {
            // Create default channels
            CreateChannel("local", "Local", ChatChannelType.Public, spatialChatRange);
            CreateChannel("global", "Global", ChatChannelType.Public, float.MaxValue);
            CreateChannel("faction", "Faction", ChatChannelType.Faction, float.MaxValue);
            CreateChannel("system", "System", ChatChannelType.System, float.MaxValue);

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            meshNetwork = GetSubsystem<MeshNetworkManager>();

            if (meshNetwork != null)
            {
                meshNetwork.OnMessageReceived += HandleNetworkMessage;
            }
        }

        /// <summary>
        /// Send a chat message on the active channel.
        /// </summary>
        public void SendMessage(string text)
        {
            SendMessage(ActiveChannelId, text);
        }

        public void SendMessage(string channelId, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (identityManager?.LocalIdentity == null) return;

            var identity = identityManager.LocalIdentity;
            var message = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString("N")[..12],
                ChannelId = channelId,
                SenderAddress = identity.DarknetAddress,
                SenderCallsign = identity.Callsign,
                SenderLevel = identity.Level,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SenderPosition = Manager.ARCamera?.transform.position ?? Vector3.zero
            };

            // Add to local channel
            AddMessageToChannel(message);

            // Broadcast via mesh
            meshNetwork?.Broadcast(MeshMessageType.Chat, JsonUtility.ToJson(message));

            OnMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// Send a direct message to a specific operative.
        /// </summary>
        public void SendDirectMessage(string targetAddress, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (identityManager?.LocalIdentity == null) return;

            // Create or get DM channel
            string dmChannelId = $"dm_{targetAddress[..8]}";
            if (!channels.ContainsKey(dmChannelId))
            {
                var targetIdentity = identityManager.GetIdentity(targetAddress);
                string name = targetIdentity?.Callsign ?? targetAddress[..8];
                CreateChannel(dmChannelId, $"DM: {name}", ChatChannelType.Direct, float.MaxValue);
            }

            var identity = identityManager.LocalIdentity;
            var message = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString("N")[..12],
                ChannelId = dmChannelId,
                SenderAddress = identity.DarknetAddress,
                SenderCallsign = identity.Callsign,
                SenderLevel = identity.Level,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsDirect = true
            };

            AddMessageToChannel(message);
            meshNetwork?.SendDirect(targetAddress, MeshMessageType.Chat, JsonUtility.ToJson(message));
            OnMessageReceived?.Invoke(message);
        }

        public void SwitchChannel(string channelId)
        {
            if (channels.ContainsKey(channelId))
            {
                ActiveChannelId = channelId;
                OnChannelChanged?.Invoke(channelId);
            }
        }

        public ChatChannel GetChannel(string channelId)
        {
            channels.TryGetValue(channelId, out var channel);
            return channel;
        }

        public IEnumerable<ChatChannel> GetAllChannels() => channels.Values;

        public List<ChatMessage> GetChannelMessages(string channelId)
        {
            if (channels.TryGetValue(channelId, out var channel))
                return channel.Messages;
            return new List<ChatMessage>();
        }

        private void CreateChannel(string id, string name, ChatChannelType type, float range)
        {
            channels[id] = new ChatChannel
            {
                ChannelId = id,
                Name = name,
                Type = type,
                Range = range,
                Messages = new List<ChatMessage>()
            };
        }

        private void AddMessageToChannel(ChatMessage message)
        {
            if (!channels.TryGetValue(message.ChannelId, out var channel))
            {
                // Auto-create channel for DMs
                CreateChannel(message.ChannelId, message.ChannelId, ChatChannelType.Direct, float.MaxValue);
                channel = channels[message.ChannelId];
            }

            channel.Messages.Add(message);
            if (channel.Messages.Count > maxMessagesPerChannel)
                channel.Messages.RemoveAt(0);
        }

        private void HandleNetworkMessage(MeshMessage netMessage)
        {
            if (netMessage.Type != MeshMessageType.Chat) return;

            try
            {
                var chatMessage = JsonUtility.FromJson<ChatMessage>(netMessage.Payload);
                if (chatMessage == null) return;

                // Skip our own messages
                if (chatMessage.SenderAddress == identityManager?.LocalIdentity?.DarknetAddress)
                    return;

                // Range check for local chat
                if (chatMessage.ChannelId == "local")
                {
                    float dist = Vector3.Distance(
                        chatMessage.SenderPosition,
                        Manager.ARCamera?.transform.position ?? Vector3.zero);
                    if (dist > spatialChatRange) return;
                }

                AddMessageToChannel(chatMessage);
                OnMessageReceived?.Invoke(chatMessage);
            }
            catch { }
        }

        protected override void OnShutdown()
        {
            if (meshNetwork != null)
                meshNetwork.OnMessageReceived -= HandleNetworkMessage;
        }
    }

    [Serializable]
    public class ChatMessage
    {
        public string MessageId;
        public string ChannelId;
        public string SenderAddress;
        public string SenderCallsign;
        public int SenderLevel;
        public string Text;
        public long Timestamp;
        public Vector3 SenderPosition;
        public bool IsDirect;
    }

    public class ChatChannel
    {
        public string ChannelId;
        public string Name;
        public ChatChannelType Type;
        public float Range;
        public List<ChatMessage> Messages;
    }

    public enum ChatChannelType
    {
        Public,     // Open to all — "local" and "global"
        Faction,    // Faction members only
        Direct,     // Private DM
        System      // System announcements
    }
}
