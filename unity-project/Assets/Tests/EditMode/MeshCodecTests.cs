// MeshCodecTests.cs - Mesh message and discovery beacon encoding

using NUnit.Framework;
using DaemonVision.Network;

namespace DaemonVision.Tests
{
    public class MeshCodecTests
    {
        [Test]
        public void MeshMessage_EncodeDecode_RoundTrips()
        {
            var original = new MeshMessage
            {
                MessageId = "0123456789abcdef",
                Type = MeshMessageType.Chat,
                SenderAddress = new string('a', 64),
                TargetAddress = MeshMessage.BroadcastTarget,
                Payload = "{\"Text\":\"hello \\\"darknet\\\"\"}",
                Timestamp = 1725150000,
                HopCount = 2,
                MaxHops = 5
            };

            var decoded = MeshNetworkManager.Decode(MeshNetworkManager.Encode(original));

            Assert.IsNotNull(decoded);
            Assert.AreEqual(original.MessageId, decoded.MessageId);
            Assert.AreEqual(original.Type, decoded.Type);
            Assert.AreEqual(original.SenderAddress, decoded.SenderAddress);
            Assert.AreEqual(original.TargetAddress, decoded.TargetAddress);
            Assert.AreEqual(original.Payload, decoded.Payload);
            Assert.AreEqual(original.Timestamp, decoded.Timestamp);
            Assert.AreEqual(original.HopCount, decoded.HopCount);
            Assert.AreEqual(original.MaxHops, decoded.MaxHops);
        }

        [Test]
        public void MeshMessage_Decode_ReturnsNullForGarbage()
        {
            Assert.IsNull(MeshNetworkManager.Decode(null));
            Assert.IsNull(MeshNetworkManager.Decode(new byte[0]));
            Assert.IsNull(MeshNetworkManager.Decode(System.Text.Encoding.UTF8.GetBytes("not json")));
            Assert.IsNull(MeshNetworkManager.Decode(new byte[] { 0xFF, 0xFE, 0x00 }));
        }

        [Test]
        public void DiscoveryBeacon_EncodeDecode_RoundTrips()
        {
            var beacon = new DiscoveryBeacon
            {
                ProtocolVersion = 1,
                DarknetAddress = new string('b', 64),
                Callsign = "GhostRunner_42",
                Level = 7,
                MeshPort = MeshNetworkManager.DefaultMeshPort,
                Timestamp = 1725150000
            };

            byte[] bytes = PeerDiscovery.EncodeBeacon(beacon);
            string text = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.IsTrue(text.StartsWith(PeerDiscovery.BeaconPrefix));

            var decoded = PeerDiscovery.DecodeBeacon(text);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(beacon.DarknetAddress, decoded.DarknetAddress);
            Assert.AreEqual(beacon.Callsign, decoded.Callsign);
            Assert.AreEqual(beacon.Level, decoded.Level);
            Assert.AreEqual(beacon.MeshPort, decoded.MeshPort);
        }

        [Test]
        public void DiscoveryBeacon_Decode_RejectsOtherPrefixes()
        {
            Assert.IsNull(PeerDiscovery.DecodeBeacon("DSPACE_GPS|1|2|3|4|5"));
            Assert.IsNull(PeerDiscovery.DecodeBeacon(""));
            Assert.IsNull(PeerDiscovery.DecodeBeacon(null));
        }

        [Test]
        public void Ports_AreDistinctAndDocumented()
        {
            Assert.AreEqual(7733, MeshNetworkManager.DefaultMeshPort);
            Assert.AreEqual(7734, PeerDiscovery.DefaultDiscoveryPort);
            Assert.AreNotEqual(MeshNetworkManager.DefaultMeshPort, PeerDiscovery.DefaultDiscoveryPort);
        }
    }
}
