// CompanionLocationReceiver.cs - Receives GPS fixes relayed by the companion phone app
// Glasses without a GPS radio (Quest 3, XREAL, Magic Leap 2) get their position
// from the phone in the operative's pocket. The companion app sends one UDP
// datagram per fix; this class listens for them and answers with an ACK so the
// phone can show that the link is alive.
//
// Packet format (pipe separated, invariant culture, one line, no trailing newline):
//   DSPACE_GPS|<lat>|<lon>|<altitudeMeters>|<accuracyMeters>|<bearingDegrees>|<unixMillis>
// The bearing field is optional; six-field packets from older companion builds
// are accepted too. The reply is:
//   DSPACE_ACK|<meshPeerCount>|<unixMillis>
//
// This is a plain class (no MonoBehaviour) so it can be unit tested and so the
// socket thread never touches Unity APIs.

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaemonVision.Spatial
{
    public struct CompanionFix
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float Accuracy;
        public float Bearing;
        public long TimestampMs;      // Phone clock, unix milliseconds
        public DateTime ReceivedUtc;  // Our clock, for staleness checks
    }

    public sealed class CompanionLocationReceiver : IDisposable
    {
        public const int DefaultPort = 7735;
        public const string PacketPrefix = "DSPACE_GPS";
        public const string AckPrefix = "DSPACE_ACK";
        private const char Separator = '|';

        private readonly object gate = new object();
        private UdpClient client;
        private CancellationTokenSource cancellation;
        private CompanionFix latest;
        private bool hasLatest;
        private long packetsReceived;

        public int Port { get; }
        public bool IsListening => client != null;
        public long PacketsReceived => Interlocked.Read(ref packetsReceived);

        /// <summary>
        /// Supplies the peer count echoed back to the phone. Read on the socket
        /// thread, so it must not touch Unity objects; a captured int is fine.
        /// </summary>
        public Func<int> PeerCountProvider { get; set; }

        public CompanionLocationReceiver(int port = DefaultPort)
        {
            Port = port;
        }

        public void Start()
        {
            if (client != null) return;

            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            client = udp;
            cancellation = new CancellationTokenSource();

            var token = cancellation.Token;
            Task.Run(() => ReceiveLoop(udp, token), token);
        }

        /// <summary>
        /// Latest fix if one has arrived within <paramref name="maxAgeSeconds"/>.
        /// </summary>
        public bool TryGetLatest(double maxAgeSeconds, out CompanionFix fix)
        {
            lock (gate)
            {
                fix = latest;
                if (!hasLatest) return false;
                double age = (DateTime.UtcNow - latest.ReceivedUtc).TotalSeconds;
                return age <= maxAgeSeconds;
            }
        }

        private async Task ReceiveLoop(UdpClient udp, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync();
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (token.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    continue;
                }

                string text;
                try
                {
                    text = Encoding.UTF8.GetString(result.Buffer);
                }
                catch
                {
                    continue;
                }

                if (!TryParse(text, out var fix)) continue;

                lock (gate)
                {
                    latest = fix;
                    hasLatest = true;
                }
                Interlocked.Increment(ref packetsReceived);

                try
                {
                    int peers = PeerCountProvider?.Invoke() ?? 0;
                    byte[] ack = Encoding.UTF8.GetBytes(BuildAck(peers, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    udp.Send(ack, ack.Length, result.RemoteEndPoint);
                }
                catch
                {
                    // The ACK is a courtesy; losing one is harmless.
                }
            }
        }

        public static bool TryParse(string packet, out CompanionFix fix)
        {
            fix = default;
            if (string.IsNullOrEmpty(packet)) return false;

            string[] parts = packet.Trim().Split(Separator);
            if (parts.Length != 6 && parts.Length != 7) return false;
            if (!string.Equals(parts[0], PacketPrefix, StringComparison.Ordinal)) return false;

            var culture = CultureInfo.InvariantCulture;
            const NumberStyles style = NumberStyles.Float;

            if (!double.TryParse(parts[1], style, culture, out double lat)) return false;
            if (!double.TryParse(parts[2], style, culture, out double lon)) return false;
            if (!double.TryParse(parts[3], style, culture, out double alt)) return false;
            if (!float.TryParse(parts[4], style, culture, out float acc)) return false;

            float bearing = 0f;
            int tsIndex = 5;
            if (parts.Length == 7)
            {
                if (!float.TryParse(parts[5], style, culture, out bearing)) return false;
                tsIndex = 6;
            }

            if (!long.TryParse(parts[tsIndex], NumberStyles.Integer, culture, out long ts)) return false;

            if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return false;
            if (double.IsNaN(alt) || float.IsNaN(acc) || acc < 0) return false;

            fix = new CompanionFix
            {
                Latitude = lat,
                Longitude = lon,
                Altitude = alt,
                Accuracy = acc,
                Bearing = bearing,
                TimestampMs = ts,
                ReceivedUtc = DateTime.UtcNow
            };
            return true;
        }

        public static string BuildPacket(double lat, double lon, double alt, float accuracy, float bearing, long unixMillis)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(Separator.ToString(),
                PacketPrefix,
                lat.ToString("F8", c),
                lon.ToString("F8", c),
                alt.ToString("F4", c),
                accuracy.ToString("F2", c),
                bearing.ToString("F1", c),
                unixMillis.ToString(c));
        }

        public static string BuildAck(int peerCount, long unixMillis)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(Separator.ToString(), AckPrefix, peerCount.ToString(c), unixMillis.ToString(c));
        }

        public void Dispose()
        {
            cancellation?.Cancel();
            client?.Close();
            client = null;
        }
    }
}
