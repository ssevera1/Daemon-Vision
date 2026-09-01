// MeshNetworkManager.cs - The Darknet mesh network
// The Daemon's darknet is a distributed peer-to-peer mesh network with NO central server.
// Operatives connect directly over the local network; messages propagate through
// the mesh with a hop limit, so if one node goes down the network routes around it.
//
// Transport today is UDP unicast to peers found by PeerDiscovery. This class owns
// the listening socket; the receive loop only copies bytes into a queue and the
// main thread parses and dispatches them during Tick().

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Network
{
    public class MeshNetworkManager : SubsystemBase
    {
        public override string Name => "MeshNetwork";

        public const int DefaultMeshPort = 7733;   // "SS" = Sobol System
        private const int MessageIdLength = 16;

        [Header("Mesh Settings")]
        [SerializeField] private float heartbeatInterval = 5f;        // seconds
        [SerializeField] private float peerTimeoutSeconds = 30f;
        [SerializeField] private int maxDirectPeers = 20;
        [SerializeField] private int messageHopLimit = 5;              // Max hops for message relay
        [SerializeField] private int maxRememberedMessageIds = 5000;   // Dedup window
        [SerializeField] private bool enableWiFiDirect = true;
        [SerializeField] private bool enableBluetooth = true;
        [SerializeField] private bool enableLocalNetwork = true;
        [SerializeField] private int meshPort = DefaultMeshPort;

        private DarknetIdentityManager identityManager;
        private PeerDiscovery peerDiscovery;

        private readonly Dictionary<string, MeshPeer> connectedPeers
            = new Dictionary<string, MeshPeer>();
        private readonly Queue<MeshMessage> outboundQueue = new Queue<MeshMessage>();

        // Raw datagrams from the socket thread. Swapped out under the lock each Tick.
        private readonly object inboundLock = new object();
        private Queue<byte[]> inboundRaw = new Queue<byte[]>();
        private Queue<byte[]> inboundDrain = new Queue<byte[]>();

        // Dedup window: a set for lookups plus a queue so the oldest ids age out
        // instead of the whole window being wiped at once.
        private readonly HashSet<string> processedMessageIds = new HashSet<string>();
        private readonly Queue<string> processedOrder = new Queue<string>();

        private UdpClient listener;
        private CancellationTokenSource cancellationSource;
        private float heartbeatTimer;

        public int MeshPort => meshPort;
        public int MaxHops => messageHopLimit;
        public bool IsListening => listener != null;
        public int ConnectedPeerCount => connectedPeers.Count;

        public event Action<MeshPeer> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<MeshMessage> OnMessageReceived;

        protected override Task OnInitialize()
        {
            UnityMainThreadDispatcher.EnsureExists();

            if (enableLocalNetwork)
            {
                try
                {
                    StartListening();
                }
                catch (Exception ex)
                {
                    Warn($"Mesh listener failed to start on port {meshPort}: {ex.Message}. " +
                         "Outbound messages will still be sent.");
                }
            }

            if (enableWiFiDirect || enableBluetooth)
                Log("WiFi Direct and Bluetooth transports are not implemented yet; using local network only.");

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
        }

        public override void Tick(float deltaTime)
        {
            heartbeatTimer += deltaTime;
            if (heartbeatTimer >= heartbeatInterval)
            {
                heartbeatTimer = 0f;
                BroadcastHeartbeat();
                PruneTimedOutPeers();
            }

            while (outboundQueue.Count > 0)
            {
                TransmitMessage(outboundQueue.Dequeue());
            }

            DrainInbound();
        }

        /// <summary>
        /// Send a message to a specific peer or broadcast to all.
        /// Messages can hop through the mesh to reach operatives not directly connected.
        /// </summary>
        public void SendMessage(MeshMessage message)
        {
            if (message == null) return;
            if (identityManager?.LocalIdentity == null) return;

            message.MessageId = Guid.NewGuid().ToString("N").Substring(0, MessageIdLength);
            message.SenderAddress = identityManager.LocalIdentity.DarknetAddress;
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            message.HopCount = 0;
            if (message.MaxHops <= 0 || message.MaxHops > messageHopLimit)
                message.MaxHops = messageHopLimit;
            if (string.IsNullOrEmpty(message.TargetAddress))
                message.TargetAddress = MeshMessage.BroadcastTarget;

            Remember(message.MessageId);
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
                TargetAddress = MeshMessage.BroadcastTarget,
                Payload = payload,
                MaxHops = messageHopLimit
            });
        }

        /// <summary>
        /// Send a direct message to a specific operative.
        /// </summary>
        public void SendDirect(string targetAddress, MeshMessageType type, string payload)
        {
            if (string.IsNullOrEmpty(targetAddress)) return;

            SendMessage(new MeshMessage
            {
                Type = type,
                TargetAddress = targetAddress,
                Payload = payload,
                MaxHops = messageHopLimit
            });
        }

        /// <summary>
        /// Called by a transport (or the listener thread) when raw data arrives.
        /// Safe to call from any thread; the datagram is parsed on the main thread.
        /// </summary>
        public void OnDataReceived(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            lock (inboundLock)
            {
                inboundRaw.Enqueue(data);
            }
        }

        public IEnumerable<MeshPeer> GetConnectedPeers() => connectedPeers.Values;

        // ----------------------------------------------------------------
        //  Inbound
        // ----------------------------------------------------------------

        private void DrainInbound()
        {
            lock (inboundLock)
            {
                if (inboundRaw.Count == 0) return;
                var full = inboundRaw;
                inboundRaw = inboundDrain;
                inboundDrain = full;
            }

            while (inboundDrain.Count > 0)
            {
                byte[] data = inboundDrain.Dequeue();
                var message = Decode(data);
                if (message == null)
                {
                    Warn("Dropped a datagram that was not a valid mesh message.");
                    continue;
                }

                if (string.IsNullOrEmpty(message.MessageId) || processedMessageIds.Contains(message.MessageId))
                    continue;

                Remember(message.MessageId);
                ProcessInboundMessage(message);
            }
        }

        private void ProcessInboundMessage(MeshMessage message)
        {
            var localAddress = identityManager?.LocalIdentity?.DarknetAddress;
            bool isBroadcast = message.TargetAddress == MeshMessage.BroadcastTarget;
            bool isForMe = isBroadcast || message.TargetAddress == localAddress;

            if (isForMe)
            {
                HandleMessage(message);
                OnMessageReceived?.Invoke(message);
            }

            // Relay if not at the hop limit. A peer cannot inflate MaxHops past
            // our own limit, which keeps a malicious message from circulating forever.
            int effectiveMaxHops = Math.Min(message.MaxHops, messageHopLimit);
            bool relay = message.HopCount < effectiveMaxHops &&
                         (isBroadcast || message.TargetAddress != localAddress);
            if (relay)
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
                default:
                    // Chat, quests, reputation, etc. are consumed by their own
                    // subsystems through OnMessageReceived.
                    break;
            }
        }

        private void HandleHeartbeat(MeshMessage message)
        {
            if (message.SenderAddress != null &&
                connectedPeers.TryGetValue(message.SenderAddress, out var peer))
            {
                peer.LastHeartbeat = Time.time;
                peer.HopDistance = message.HopCount;
            }
        }

        private void HandleIdentityBroadcast(MeshMessage message)
        {
            if (string.IsNullOrEmpty(message.Payload)) return;

            try
            {
                var identity = JsonUtility.FromJson<DarknetIdentity>(message.Payload);
                if (identity != null && identity.DarknetAddress == message.SenderAddress)
                {
                    identityManager?.RegisterPeerIdentity(identity);
                }
            }
            catch (Exception ex)
            {
                Warn($"Ignored malformed identity broadcast from {AddressUtil.Short(message.SenderAddress)}: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        //  Outbound
        // ----------------------------------------------------------------

        private void BroadcastHeartbeat()
        {
            if (identityManager?.LocalIdentity == null) return;

            Broadcast(MeshMessageType.Heartbeat, "");
            Broadcast(MeshMessageType.IdentityBroadcast, JsonUtility.ToJson(identityManager.LocalIdentity));
        }

        private void TransmitMessage(MeshMessage message)
        {
            if (connectedPeers.Count == 0) return;

            byte[] data = Encode(message);

            if (message.TargetAddress != MeshMessage.BroadcastTarget &&
                connectedPeers.TryGetValue(message.TargetAddress, out var direct))
            {
                direct.Transport?.Send(data);
                return;
            }

            // Broadcast, or a target we are not directly connected to: fan out and
            // let the mesh relay it.
            foreach (var peer in connectedPeers.Values)
                peer.Transport?.Send(data);
        }

        private void RelayMessage(MeshMessage message)
        {
            if (connectedPeers.Count == 0) return;

            byte[] data = Encode(message);
            foreach (var peer in connectedPeers.Values)
            {
                if (peer.Address != message.SenderAddress)
                    peer.Transport?.Send(data);
            }
        }

        // ----------------------------------------------------------------
        //  Peers
        // ----------------------------------------------------------------

        private void HandlePeerDiscovered(DiscoveredPeer discovered)
        {
            if (discovered == null || string.IsNullOrEmpty(discovered.Address)) return;

            if (connectedPeers.TryGetValue(discovered.Address, out var existing))
            {
                // Same operative, possibly a new endpoint after a network change.
                if (!ReferenceEquals(existing.Transport, discovered.Transport))
                {
                    existing.Transport?.Disconnect();
                    existing.Transport = discovered.Transport;
                }
                existing.LastHeartbeat = Time.time;
                return;
            }

            if (connectedPeers.Count >= maxDirectPeers)
            {
                Warn($"Max peer limit ({maxDirectPeers}) reached. Ignoring {discovered.DisplayName}.");
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
            Log($"Peer connected: {discovered.DisplayName} [{AddressUtil.Short(discovered.Address)}]");
            OnPeerConnected?.Invoke(peer);
        }

        private void HandlePeerLost(string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            if (connectedPeers.TryGetValue(address, out var peer))
            {
                connectedPeers.Remove(address);
                peer.Transport?.Disconnect();
                identityManager?.RemovePeerIdentity(address);
                OnPeerDisconnected?.Invoke(address);
            }
        }

        private void PruneTimedOutPeers()
        {
            List<string> timedOut = null;
            foreach (var kvp in connectedPeers)
            {
                if (Time.time - kvp.Value.LastHeartbeat > peerTimeoutSeconds)
                    (timedOut ??= new List<string>()).Add(kvp.Key);
            }

            if (timedOut == null) return;
            foreach (var address in timedOut)
            {
                Log($"Peer timed out: {AddressUtil.Short(address)}");
                HandlePeerLost(address);
            }
        }

        private void Remember(string messageId)
        {
            if (!processedMessageIds.Add(messageId)) return;
            processedOrder.Enqueue(messageId);

            while (processedOrder.Count > maxRememberedMessageIds)
                processedMessageIds.Remove(processedOrder.Dequeue());
        }

        // ----------------------------------------------------------------
        //  Socket
        // ----------------------------------------------------------------

        private void StartListening()
        {
            listener = new UdpClient();
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, meshPort));

            cancellationSource = new CancellationTokenSource();
            var socket = listener;
            var token = cancellationSource.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await socket.ReceiveAsync();
                        OnDataReceived(result.Buffer);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MeshNetwork] Receive error: {ex.Message}");
                    }
                }
            }, token);

            Log($"Mesh network listening on UDP port {meshPort}");
        }

        // ----------------------------------------------------------------
        //  Encoding. Static so tests and other transports share one format.
        // ----------------------------------------------------------------

        public static byte[] Encode(MeshMessage message)
        {
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        }

        public static MeshMessage Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return null;

            try
            {
                string json = Encoding.UTF8.GetString(data);
                if (string.IsNullOrWhiteSpace(json) || json[0] != '{') return null;
                return JsonUtility.FromJson<MeshMessage>(json);
            }
            catch
            {
                return null;
            }
        }

        protected override void OnShutdown()
        {
            if (peerDiscovery != null)
            {
                peerDiscovery.OnPeerDiscovered -= HandlePeerDiscovered;
                peerDiscovery.OnPeerLost -= HandlePeerLost;
            }

            cancellationSource?.Cancel();
            listener?.Close();
            listener = null;

            foreach (var peer in connectedPeers.Values)
                peer.Transport?.Disconnect();
            connectedPeers.Clear();
        }
    }

    [Serializable]
    public class MeshMessage
    {
        public const string BroadcastTarget = "*";

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
    /// Transport abstraction - allows different physical layers (WiFi Direct, BLE, UDP).
    /// </summary>
    public interface IMeshTransport
    {
        void Send(byte[] data);
        void Disconnect();
        bool IsConnected { get; }
    }
}
