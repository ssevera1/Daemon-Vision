// PeerDiscovery.cs - Discover nearby darknet operatives
// Broadcasts a UDP beacon on the local network and listens for beacons from
// other operatives. WiFi Direct and BLE transports are roadmap items; today the
// mesh forms over whatever LAN or hotspot the glasses are on.
//
// Threading: the UDP receive loop runs on a thread-pool thread and only reads
// bytes. Everything that touches Unity state runs on the main thread via
// UnityMainThreadDispatcher.

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

        public const string BeaconPrefix = "DSPACE:";
        public const int DefaultDiscoveryPort = 7734;

        [Header("Discovery Settings")]
        [SerializeField] private float broadcastInterval = 3f;
        [SerializeField] private int discoveryPort = DefaultDiscoveryPort;
        [SerializeField] private float peerTimeoutSeconds = 15f;
        [SerializeField] private bool enableUDPBroadcast = true;
        [SerializeField] private bool enableBLEDiscovery = true;
        [SerializeField] private bool enableWiFiDirect = true;

        private DarknetIdentityManager identityManager;
        private MeshNetworkManager meshNetwork;
        private UdpClient udpListener;
        private UdpClient udpBroadcaster;
        private CancellationTokenSource cancellationSource;
        private float broadcastTimer;
        private float pruneTimer;

        private readonly Dictionary<string, DiscoveredPeer> discoveredPeers
            = new Dictionary<string, DiscoveredPeer>();

        public int DiscoveryPort => discoveryPort;
        public int DiscoveredPeerCount => discoveredPeers.Count;

        public event Action<DiscoveredPeer> OnPeerDiscovered;
        public event Action<string> OnPeerLost;

        protected override Task OnInitialize()
        {
            UnityMainThreadDispatcher.EnsureExists();
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

            if (enableBLEDiscovery || enableWiFiDirect)
                Log("BLE and WiFi Direct discovery are not implemented yet; using UDP only.");

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            meshNetwork = GetSubsystem<MeshNetworkManager>();
        }

        public override void Tick(float deltaTime)
        {
            broadcastTimer += deltaTime;
            if (broadcastTimer >= broadcastInterval)
            {
                broadcastTimer = 0f;
                BroadcastPresence();
            }

            pruneTimer += deltaTime;
            if (pruneTimer >= 1f)
            {
                pruneTimer = 0f;
                PruneSilentPeers();
            }
        }

        public IEnumerable<DiscoveredPeer> GetDiscoveredPeers() => discoveredPeers.Values;

        private void StartUDPDiscovery()
        {
            // ReuseAddress lets two D-Space instances share a machine during
            // development; on a headset there is only ever one.
            udpListener = new UdpClient();
            udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            udpListener.EnableBroadcast = true;

            var listener = udpListener;
            var token = cancellationSource.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await listener.ReceiveAsync();
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PeerDiscovery] UDP receive error: {ex.Message}");
                        continue;
                    }

                    string message;
                    try
                    {
                        message = Encoding.UTF8.GetString(result.Buffer);
                    }
                    catch
                    {
                        continue;
                    }

                    var sender = result.RemoteEndPoint;
                    UnityMainThreadDispatcher.Enqueue(() => HandleDiscoveryMessage(message, sender));
                }
            }, token);

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
                MeshPort = meshNetwork?.MeshPort ?? MeshNetworkManager.DefaultMeshPort,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            try
            {
                byte[] data = EncodeBeacon(beacon);
                var endpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                udpBroadcaster.Send(data, data.Length, endpoint);
            }
            catch (Exception ex)
            {
                Warn($"Broadcast failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs on the main thread. Registers new peers and refreshes last-seen times.
        /// </summary>
        private void HandleDiscoveryMessage(string message, IPEndPoint sender)
        {
            if (!IsActive) return;

            var beacon = DecodeBeacon(message);
            if (beacon == null || string.IsNullOrEmpty(beacon.DarknetAddress)) return;

            // Don't discover ourselves (our own broadcast loops back on most stacks)
            if (beacon.DarknetAddress == identityManager?.LocalIdentity?.DarknetAddress)
                return;

            if (discoveredPeers.TryGetValue(beacon.DarknetAddress, out var known))
            {
                known.LastSeenTime = Time.time;
                known.Level = beacon.Level;
                known.DisplayName = beacon.Callsign;
                return;
            }

            int meshPort = beacon.MeshPort > 0 ? beacon.MeshPort : MeshNetworkManager.DefaultMeshPort;
            var peer = new DiscoveredPeer
            {
                Address = beacon.DarknetAddress,
                DisplayName = beacon.Callsign,
                IPEndPoint = sender,
                MeshPort = meshPort,
                Level = beacon.Level,
                DiscoveredTime = Time.time,
                LastSeenTime = Time.time,
                Transport = new UDPMeshTransport(sender.Address, meshPort)
            };

            discoveredPeers[beacon.DarknetAddress] = peer;
            Log($"Discovered {beacon.Callsign} [{AddressUtil.Short(beacon.DarknetAddress)}] at {sender.Address}");
            OnPeerDiscovered?.Invoke(peer);
        }

        private void PruneSilentPeers()
        {
            if (discoveredPeers.Count == 0) return;

            List<string> lost = null;
            foreach (var kvp in discoveredPeers)
            {
                if (Time.time - kvp.Value.LastSeenTime > peerTimeoutSeconds)
                    (lost ??= new List<string>()).Add(kvp.Key);
            }

            if (lost == null) return;
            foreach (var address in lost)
            {
                if (discoveredPeers.TryGetValue(address, out var peer))
                {
                    peer.Transport?.Disconnect();
                    discoveredPeers.Remove(address);
                }
                Log($"Peer lost: {AddressUtil.Short(address)}");
                OnPeerLost?.Invoke(address);
            }
        }

        // ----------------------------------------------------------------
        //  Beacon encoding. Static so it can be unit tested and so the
        //  companion app's Java side can be checked against the same format.
        // ----------------------------------------------------------------

        public static byte[] EncodeBeacon(DiscoveryBeacon beacon)
        {
            string json = JsonUtility.ToJson(beacon);
            return Encoding.UTF8.GetBytes(BeaconPrefix + json);
        }

        public static DiscoveryBeacon DecodeBeacon(string message)
        {
            if (string.IsNullOrEmpty(message) || !message.StartsWith(BeaconPrefix, StringComparison.Ordinal))
                return null;

            try
            {
                return JsonUtility.FromJson<DiscoveryBeacon>(message.Substring(BeaconPrefix.Length));
            }
            catch
            {
                return null;
            }
        }

        protected override void OnShutdown()
        {
            cancellationSource?.Cancel();
            udpListener?.Close();
            udpBroadcaster?.Close();
            udpListener = null;
            udpBroadcaster = null;

            foreach (var peer in discoveredPeers.Values)
                peer.Transport?.Disconnect();
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
        private readonly IPEndPoint target;
        private UdpClient client;

        public bool IsConnected => client != null;
        public IPEndPoint Target => target;

        public UDPMeshTransport(IPAddress host, int port)
        {
            target = new IPEndPoint(host, port);
            client = new UdpClient();
        }

        public void Send(byte[] data)
        {
            var c = client;
            if (c == null || data == null || data.Length == 0) return;

            try
            {
                c.Send(data, data.Length, target);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UDPMeshTransport] Send to {target} failed: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            client?.Close();
            client = null;
        }
    }
}
