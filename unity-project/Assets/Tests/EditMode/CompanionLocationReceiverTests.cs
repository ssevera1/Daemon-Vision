// CompanionLocationReceiverTests.cs - Wire format shared with the Android companion app

using NUnit.Framework;
using DaemonVision.Spatial;

namespace DaemonVision.Tests
{
    public class CompanionLocationReceiverTests
    {
        [Test]
        public void TryParse_AcceptsSevenFieldPacket()
        {
            const string packet = "DSPACE_GPS|37.77490000|-122.41940000|10.5000|4.20|271.5|1725150000000";

            Assert.IsTrue(CompanionLocationReceiver.TryParse(packet, out var fix));
            Assert.AreEqual(37.7749, fix.Latitude, 1e-6);
            Assert.AreEqual(-122.4194, fix.Longitude, 1e-6);
            Assert.AreEqual(10.5, fix.Altitude, 1e-6);
            Assert.AreEqual(4.2f, fix.Accuracy, 1e-4f);
            Assert.AreEqual(271.5f, fix.Bearing, 1e-4f);
            Assert.AreEqual(1725150000000L, fix.TimestampMs);
        }

        [Test]
        public void TryParse_AcceptsLegacySixFieldPacket()
        {
            const string packet = "DSPACE_GPS|40.7580|-73.9855|5.0|8.0|1725150000000";

            Assert.IsTrue(CompanionLocationReceiver.TryParse(packet, out var fix));
            Assert.AreEqual(40.7580, fix.Latitude, 1e-6);
            Assert.AreEqual(0f, fix.Bearing);
            Assert.AreEqual(1725150000000L, fix.TimestampMs);
        }

        [TestCase("")]
        [TestCase("DSPACE_ACK|3|1725150000000")]
        [TestCase("DSPACE_GPS|abc|-73.9855|5.0|8.0|1725150000000")]
        [TestCase("DSPACE_GPS|95.0|-73.9855|5.0|8.0|1725150000000")]
        [TestCase("DSPACE_GPS|40.7580|-73.9855|5.0|-1|1725150000000")]
        [TestCase("DSPACE_GPS|40.7580|-73.9855")]
        [TestCase("garbage")]
        public void TryParse_RejectsMalformedInput(string packet)
        {
            Assert.IsFalse(CompanionLocationReceiver.TryParse(packet, out _));
        }

        [Test]
        public void BuildPacket_RoundTripsThroughTryParse()
        {
            string packet = CompanionLocationReceiver.BuildPacket(51.5074, -0.1278, 15.25, 3.5f, 88.0f, 1725150001234L);

            Assert.IsTrue(packet.StartsWith(CompanionLocationReceiver.PacketPrefix + "|"));
            Assert.IsTrue(CompanionLocationReceiver.TryParse(packet, out var fix));
            Assert.AreEqual(51.5074, fix.Latitude, 1e-6);
            Assert.AreEqual(-0.1278, fix.Longitude, 1e-6);
            Assert.AreEqual(15.25, fix.Altitude, 1e-3);
            Assert.AreEqual(3.5f, fix.Accuracy, 1e-3f);
            Assert.AreEqual(88.0f, fix.Bearing, 1e-3f);
            Assert.AreEqual(1725150001234L, fix.TimestampMs);
        }

        [Test]
        public void BuildAck_MatchesCompanionFormat()
        {
            Assert.AreEqual("DSPACE_ACK|7|1725150000000", CompanionLocationReceiver.BuildAck(7, 1725150000000L));
        }

        [Test]
        public void DefaultPort_MatchesDocumentedPort()
        {
            // docs/BUILDING.md and the companion app's RelayProtocol.java both say 7735.
            Assert.AreEqual(7735, CompanionLocationReceiver.DefaultPort);
        }
    }
}
