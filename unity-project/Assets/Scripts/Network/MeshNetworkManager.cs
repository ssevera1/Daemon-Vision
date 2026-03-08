// MeshNetworkManager.cs — The Darknet mesh network
// The Daemon's darknet is a distributed peer-to-peer mesh network with NO central server.
// Operatives connect directly via WiFi Direct, Bluetooth, and local network discovery.
// Messages propagate through the mesh — if one node goes down, the network routes around it.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Network
{
    public class MeshNetworkManager : SubsystemBase
    {
        public override string Name => "MeshNetwork";

        [Header("Mesh Settings")]
        [SerializeField] private float heartbeatInterval = 5f;        // seconds
        [SerializeField] private float peerTimeoutSeconds = 30f;
        [SerializeField] private int maxDirectPeers = 20;
        [SerializeField] private int messageHopLimit = 5;              // Max hops for message relay
        [SerializeField] private bool enableWiFiDirect = true;
        [SerializeField] private bool enableBluetooth = true;
        [SerializeField] private bool enableLocalNetwork = true;
        [SerializeField] private int meshPort = 7733;                  // "SS" = Sobol System

        private DarknetIdentityManager identityManager;
        private PeerDiscovery peerDiscovery;

        private readonly Dictionary<string, MeshPeer> connectedPeers
            = new Dictionary<string, MeshPeer>();
        private readonly Queue<MeshMessage> outboundQueue = new Queue<MeshMessage>();
        private readonly Queue<MeshMessage> inboundQueue = new Queue<MeshMessage>();
        private readonly HashSet<string> processedMessageIds = new HashSet<string>();

        private float heartbeatTimer;

        public int ConnectedPeerCount => connectedPeers.Count;

        public event Action<MeshPeer> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<MeshMessage> OnMessageReceived;

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            peerDiscovery = GetSubsystem<PeerDiscovery>();

            if (peerDiscovery != null)
            {
                peerDiscovery.OnPeerDiscovered += HandlePeerDiscovered;
                peerDiscovery.OnPeerLost += HandlePeerLost;
            }

            StartListening();
        }

        public override void Tick(float deltaTime)
        {
            heartbeatTimer += deltaTime;

            // Send heartbeats
            if (heartbeatTimer >= heartbeatInterval)
            {
                heartbeatTimer = 0f;
                BroadcastHeartbeat();
                PruneTimedOutPeers();
            }

            // Process outbound messages
            while (outboundQueue.Count > 0)
            {
                var msg = outboundQueue.Dequeue();
                TransmitMessage(msg);
            }

            // Process inbound messages
            while (inboundQueue.Count > 0)
            {
                var msg = inboundQueue.Dequeue();
                ProcessInboundMessage(msg);
            }
        }

        /// <summary>
        /// Send a message to a specific peer or broadcast to all.
        /// Messages can hop through the mesh to reach operatives not directly connected.
        /// </summary>
        public void SendMessage(MeshMessage message)
        {
            if (identityManager?.LocalIdentity == null) return;

            message.MessageId = Guid.NewGuid().ToString("N")[..16];
            message.SenderAddress = identityManager.LocalIdentity.DarknetAddress;
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            message.HopCount = 0;

            processedMessageIds.Add(message.MessageId);
            outboundQueue.Enqueue(message);
        }

        /// <summary>
        /// Broadcast a message to all connected peers (and beyond via relay).
        /// </summary>
        public void Broadcast(MeshMessageType type, string payload)
        {
            SendMessage(new MeshMessage
            {
                Type = type,
                TargetAddress = "*", // Broadcast
                Payload = payload,
                MaxHops = messageHopLimit
            });
        }

        /// <summary>
        /// Send a direct message to a specific operative.
        /// </summary>
        public void SendDirect(string targetAddress, MeshMessageType type, string payload)
        {
            SendMessage(new MeshMessage
            {
                Type = type,
                TargetAddress = targetAddress,
                Payload = payload,
                MaxHops = messageHopLimit
            });
        }

        /// <summary>
        /// Called by the transport layer when raw data arrives from a peer.
        /// </summary>
        public void OnDataReceived(string fromPeerAddress, byte[] data)
        {
            try
            {
                string json = System.Text.Encoding.UTF8.GetString(data);
                var message = JsonUtility.FromJson<MeshMessage>(json);

                if (message == null) return;

                // Deduplication — prevent message loops
                if (processedMessageIds.Contains(message.MessageId))
                    return;

                processedMessageIds.Add(message.MessageId);
                inboundQueue.Enqueue(message);
            }
            catch (Exception ex)
            {
                Warn($"Failed to parse message from {fromPeerAddress[..8]}...: {ex.Message}");
            }
        }

        private void ProcessInboundMessage(MeshMessage message)
        {
            var localAddress = identityManager?.LocalIdentity?.DarknetAddress;
            bool isForMe = message.TargetAddress == "*" || message.TargetAddress == localAddress;

            if (isForMe)
            {
                OnMessageReceived?.Invoke(message);
                HandleMessage(message);
            }

            // Relay if not at hop limit (mesh forwarding)
            if (message.HopCount < message.MaxHops && message.TargetAddress != localAddress)
            {
                message.HopCount++;
                RelayMessage(message);
            }
        }

        private void HandleMessage(MeshMessage message)
        {
            switch (message.Type)
            {
                case MeshMessageType.Heartbeat:
                    HandleHeartbeat(message);
                    break;
                case MeshMessageType.IdentityBroadcast:
                    HandleIdentityBroadcast(message);
                    break;
                case MeshMessageType.Chat:
                case MeshMessageType.QuestBroadcast:
                case MeshMessageType.ReputationUpdate:
                case MeshMessageType.ThreatAlert:
                case MeshMessageType.CreditTransfer:
                case MeshMessageType.AnchorSync:
                    // These are handled by their respective subsystems via OnMessageReceived
                    break;
            }
        }

        private void HandleHeartbeat(MeshMessage message)
        {
            if (connectedPeers.TryGetValue(message.SenderAddress, out var peer))
            {
                peer.LastHeartbeat = Time.time;
                peer.HopDistance = message.HopCount;
            }
        }

        private void HandleIdentityBroadcast(MeshMessage message)
        {
            try
            {
                var identity = JsonUtility.FromJson<DarknetIdentity>(message.Payload);
                if (identity != null)
                {
                    identityManager?.RegisterPeerIdentity(identity);
                }
            }
            catch { }
        }

        private void BroadcastHeartbeat()
        {
            Broadcast(MeshMessageType.Heartbeat, "");

            // Also broadcast our identity periodically
            if (identityManager?.LocalIdentity != null)
            {
                Broadcast(MeshMessageType.IdentityBroadcast,
                    JsonUtility.ToJson(identityManager.LocalIdentity));
            }
        }

        private void TransmitMessage(MeshMessage message)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));

            if (message.TargetAddress == "*")
            {
                // Broadcast to all connected peers
                foreach (var peer in connectedPeers.Values)
                {
                    peer.Transport?.Send(data);
                }
            }
            else
            {
                // Direct send — or relay through nearest peer
                if (connectedPeers.TryGetValue(message.TargetAddress, out var peer))
                {
                    peer.Transport?.Send(data);
                }
                else
                {
                    // Not directly connected — relay through all peers
                    foreach (var p in connectedPeers.Values)
                        p.Transport?.Send(data);
                }
            }
        }

        private void RelayMessage(MeshMessage message)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            foreach (var peer in connectedPeers.Values)
            {
                if (peer.Address != message.SenderAddress)
                    peer.Transport?.Send(data);
            }
        }

        private void HandlePeerDiscovered(DiscoveredPeer discovered)
        {
            if (connectedPeers.ContainsKey(discovered.Address))
                return;

            if (connectedPeers.Count >= maxDirectPeers)
            {
                Warn("Max peer limit reached. Ignoring new peer.");
                return;
            }

            var peer = new MeshPeer
            {
                Address = discovered.Address,
                DisplayName = discovered.DisplayName,
                LastHeartbeat = Time.time,
                HopDistance = 0,
                Transport = discovered.Transport
            };

            connectedPeers[discovered.Address] = peer;
            OnPeerConnected?.Invoke(peer);
            Log($"Peer connected: {discovered.DisplayName} [{discovered.Address[..8]}...]");
        }

        private void HandlePeerLost(string address)
        {
            if (connectedPeers.Remove(address))
            {
                OnPeerDisconnected?.Invoke(address);
                identityManager?.RemovePeerIdentity(address);
            }
        }

        private void PruneTimedOutPeers()
        {
            var timedOut = new List<string>();
            foreach (var kvp in connectedPeers)
            {
                if (Time.time - kvp.Value.LastHeartbeat > peerTimeoutSeconds)
                    timedOut.Add(kvp.Key);
            }

            foreach (var address in timedOut)
                HandlePeerLost(address);

            // Prune old message IDs to prevent memory leak
            if (processedMessageIds.Count > 10000)
                processedMessageIds.Clear();
        }

        private void StartListening()
        {
            Log($"Mesh network listening on port {meshPort}");
            // In production: start UDP/TCP listeners, WiFi Direct service, BLE advertising
        }

        protected override void OnShutdown()
        {
            foreach (var peer in connectedPeers.Values)
                peer.Transport?.Disconnect();
            connectedPeers.Clear();
        }
    }

    [Serializable]
    public class MeshMessage
    {
        public string MessageId;
        public MeshMessageType Type;
        public string SenderAddress;
        public string TargetAddress;    // "*" for broadcast
        public string Payload;          // JSON payload
        public long Timestamp;
        public int HopCount;
        public int MaxHops;
    }

    public enum MeshMessageType
    {
        Heartbeat,
        IdentityBroadcast,
        Chat,
        QuestBroadcast,
        ReputationUpdate,
        ThreatAlert,
        CreditTransfer,
        AnchorSync,
        VoiceData,
        FactionMessage,
        SystemAlert
    }

    public class MeshPeer
    {
        public string Address;
        public string DisplayName;
        public float LastHeartbeat;
        public int HopDistance;
        public IMeshTransport Transport;
    }

    /// <summary>
    /// Transport abstraction — allows different physical layers (WiFi Direct, BLE, TCP).
    /// </summary>
    public interface IMeshTransport
    {
        void Send(byte[] data);
        void Disconnect();
        bool IsConnected { get; }
    }
}
