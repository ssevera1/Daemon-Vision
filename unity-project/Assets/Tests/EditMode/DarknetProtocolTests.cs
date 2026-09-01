// DarknetProtocolTests.cs - Round-trip and tamper tests for the hybrid RSA/AES envelope

using System.Security.Cryptography;
using NUnit.Framework;
using DaemonVision.Network;

namespace DaemonVision.Tests
{
    public class DarknetProtocolTests
    {
        private RSACryptoServiceProvider recipient;
        private string recipientPublicKey;

        [SetUp]
        public void SetUp()
        {
            recipient = new RSACryptoServiceProvider(2048);
            recipientPublicKey = recipient.ToXmlString(false);
        }

        [TearDown]
        public void TearDown()
        {
            recipient.Dispose();
        }

        [Test]
        public void EncryptThenDecrypt_RoundTripsUnicodeText()
        {
            const string plaintext = "Sobol says: the darknet is watching. ★★★";

            byte[] envelope = DarknetProtocol.EncryptForRecipient(plaintext, recipientPublicKey);
            string decrypted = DarknetProtocol.DecryptWithKey(envelope, recipient);

            Assert.AreEqual(plaintext, decrypted);
        }

        [Test]
        public void Encrypt_UsesRandomKeyAndIvPerMessage()
        {
            byte[] a = DarknetProtocol.EncryptForRecipient("same text", recipientPublicKey);
            byte[] b = DarknetProtocol.EncryptForRecipient("same text", recipientPublicKey);

            Assert.AreNotEqual(a, b, "two envelopes for the same plaintext must differ");
        }

        [Test]
        public void Envelope_StartsWithWireVersion()
        {
            byte[] envelope = DarknetProtocol.EncryptForRecipient("x", recipientPublicKey);
            Assert.AreEqual(DarknetProtocol.WireVersion, envelope[0]);
        }

        [Test]
        public void Decrypt_WithWrongKey_Throws()
        {
            byte[] envelope = DarknetProtocol.EncryptForRecipient("secret", recipientPublicKey);

            using (var stranger = new RSACryptoServiceProvider(2048))
            {
                Assert.Throws<CryptographicException>(() => DarknetProtocol.DecryptWithKey(envelope, stranger));
            }
        }

        [Test]
        public void Decrypt_RejectsTruncatedInput()
        {
            byte[] envelope = DarknetProtocol.EncryptForRecipient("secret", recipientPublicKey);
            var truncated = new byte[10];
            System.Array.Copy(envelope, truncated, truncated.Length);

            Assert.Throws<CryptographicException>(() => DarknetProtocol.DecryptWithKey(truncated, recipient));
        }

        [Test]
        public void Decrypt_RejectsBogusKeyLength()
        {
            byte[] envelope = DarknetProtocol.EncryptForRecipient("secret", recipientPublicKey);
            envelope[1] = 0xFF;
            envelope[2] = 0xFF;

            Assert.Throws<CryptographicException>(() => DarknetProtocol.DecryptWithKey(envelope, recipient));
        }

        [Test]
        public void Decrypt_RejectsUnknownWireVersion()
        {
            byte[] envelope = DarknetProtocol.EncryptForRecipient("secret", recipientPublicKey);
            envelope[0] = 99;

            Assert.Throws<CryptographicException>(() => DarknetProtocol.DecryptWithKey(envelope, recipient));
        }
    }
}
