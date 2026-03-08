// PeerDiscovery.cs — Discover nearby darknet operatives
// Uses multiple transport layers: WiFi Direct, BLE, mDNS/Bonjour, and UDP broadcast
// to find other D-Space users on the local network. In the Daemon, the mesh
// is self-organizing — nodes discover each other automatically.

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
    public class PeerDiscovery : SubsystemBase
    {
        public override string Name => "PeerDiscovery";

        [Header("Discovery Settings")]
        [SerializeField] private float broadcastInterval = 3f;
        [SerializeField] private int discoveryPort = 7734;
        [SerializeField] private bool enableUDPBroadcast = true;
        [SerializeField] private bool enableBLEDiscovery = true;
        [SerializeField] private bool enableWiFiDirect = true;

        private DarknetIdentityManager identityManager;
        private UdpClient udpListener;
        private UdpClient udpBroadcaster;
        private CancellationTokenSource cancellationSource;
        private float broadcastTimer;

        private readonly Dictionary<string, DiscoveredPeer> discoveredPeers
            = new Dictionary<string, DiscoveredPeer>();

        public event Action<DiscoveredPeer> OnPeerDiscovered;
        public event Action<string> OnPeerLost;

        protected override async Task OnInitialize()
        {
            cancellationSource = new CancellationTokenSource();

            if (enableUDPBroadcast)
            {
                try
                {
                    StartUDPDiscovery();
                }
                catch (Exception ex)
                {
                    Warn($"UDP discovery failed to start: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
        }

        public override void Tick(float deltaTime)
        {
            broadcastTimer += deltaTime;
            if (broadcastTimer >= broadcastInterval)
            {
                broadcastTimer = 0f;
                BroadcastPresence();
            }
        }

        private void StartUDPDiscovery()
        {
            // Listener — receive discovery broadcasts from other operatives
            udpListener = new UdpClient(discoveryPort);
            udpListener.EnableBroadcast = true;

            // Start async receive loop
            Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udpListener.ReceiveAsync();
                        string message = Encoding.UTF8.GetString(result.Buffer);
                        HandleDiscoveryMessage(message, result.RemoteEndPoint);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PeerDiscovery] UDP receive error: {ex.Message}");
                    }
                }
            }, cancellationSource.Token);

            // Broadcaster
            udpBroadcaster = new UdpClient();
            udpBroadcaster.EnableBroadcast = true;

            Log($"UDP discovery active on port {discoveryPort}");
        }

        private void BroadcastPresence()
        {
            if (udpBroadcaster == null) return;
            if (identityManager?.LocalIdentity == null) return;

            var beacon = new DiscoveryBeacon
            {
                ProtocolVersion = 1,
                DarknetAddress = identityManager.LocalIdentity.DarknetAddress,
                Callsign = identityManager.LocalIdentity.Callsign,
                Level = identityManager.LocalIdentity.Level,
                MeshPort = 7733,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            try
            {
                string json = JsonUtility.ToJson(beacon);
                byte[] data = Encoding.UTF8.GetBytes($"DSPACE:{json}");
                var endpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                udpBroadcaster.Send(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                Warn($"Broadcast failed: {ex.Message}");
            }
        }

        private void HandleDiscoveryMessage(string message, IPEndPoint sender)
        {
            if (!message.StartsWith("DSPACE:")) return;

            string json = message.Substring(7);
            try
            {
                var beacon = JsonUtility.FromJson<DiscoveryBeacon>(json);
                if (beacon == null) return;

                // Don't discover ourselves
                if (beacon.DarknetAddress == identityManager?.LocalIdentity?.DarknetAddress)
                    return;

                if (!discoveredPeers.ContainsKey(beacon.DarknetAddress))
                {
                    var peer = new DiscoveredPeer
                    {
                        Address = beacon.DarknetAddress,
                        DisplayName = beacon.Callsign,
                        IPEndPoint = sender,
                        MeshPort = beacon.MeshPort,
                        Level = beacon.Level,
                        DiscoveredTime = Time.time,
                        Transport = new UDPMeshTransport(sender.Address.ToString(), beacon.MeshPort)
                    };

                    discoveredPeers[beacon.DarknetAddress] = peer;

                    // Dispatch to main thread
                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        OnPeerDiscovered?.Invoke(peer);
                    });
                }
                else
                {
                    // Update last seen time
                    discoveredPeers[beacon.DarknetAddress].LastSeenTime = Time.time;
                }
            }
            catch { }
        }

        protected override void OnShutdown()
        {
            cancellationSource?.Cancel();
            udpListener?.Close();
            udpBroadcaster?.Close();
            discoveredPeers.Clear();
        }
    }

    [Serializable]
    public class DiscoveryBeacon
    {
        public int ProtocolVersion;
        public string DarknetAddress;
        public string Callsign;
        public int Level;
        public int MeshPort;
        public long Timestamp;
    }

    public class DiscoveredPeer
    {
        public string Address;
        public string DisplayName;
        public IPEndPoint IPEndPoint;
        public int MeshPort;
        public int Level;
        public float DiscoveredTime;
        public float LastSeenTime;
        public IMeshTransport Transport;
    }

    /// <summary>
    /// UDP-based mesh transport for local network communication.
    /// </summary>
    public class UDPMeshTransport : IMeshTransport
    {
        private readonly string host;
        private readonly int port;
        private UdpClient client;

        public bool IsConnected => client != null;

        public UDPMeshTransport(string host, int port)
        {
            this.host = host;
            this.port = port;
            client = new UdpClient();
        }

        public void Send(byte[] data)
        {
            try
            {
                client?.Send(data, data.Length, host, port);
            }
            catch { }
        }

        public void Disconnect()
        {
            client?.Close();
            client = null;
        }
    }

    /// <summary>
    /// Simple main-thread dispatcher for callbacks from network threads.
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> executionQueue = new Queue<Action>();
        private static UnityMainThreadDispatcher instance;

        public static void Enqueue(Action action)
        {
            lock (executionQueue)
            {
                executionQueue.Enqueue(action);
            }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Update()
        {
            lock (executionQueue)
            {
                while (executionQueue.Count > 0)
                    executionQueue.Dequeue().Invoke();
            }
        }
    }
}
