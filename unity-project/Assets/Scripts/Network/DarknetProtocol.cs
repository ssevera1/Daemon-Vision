// DarknetProtocol.cs - Encrypted communication protocol for the darknet
// All D-Space communication is encrypted end-to-end. The Daemon's network
// uses strong encryption to prevent interception and ensure operative privacy.
//
// Wire format produced by Encrypt():
//   [version:1] [keyLen:2 little-endian] [RSA(aesKey)] [iv:16] [AES-CBC(plaintext)]
// The IV is not secret, so it travels in the clear; only the AES key is wrapped
// with the recipient's RSA public key.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Network
{
    public class DarknetProtocol : SubsystemBase
    {
        public override string Name => "DarknetProtocol";

        public const byte WireVersion = 1;
        private const int RsaKeySizeBits = 2048;
        private const int AesKeySizeBits = 256;
        private const int IvLength = 16;
        private const int HeaderLength = 1 + 2;
        private const string PrivateKeyPref = "darknet_private_key";

        // OAEP with SHA-1 is the strongest padding RSACryptoServiceProvider supports
        // on Mono, the runtime Unity ships on every player platform. OaepSHA256
        // throws CryptographicException there, which made every Encrypt() call
        // fail on device. SHA-1 inside OAEP is still considered sound because the
        // hash is used as a mask generator, not for collision resistance.
        private static readonly RSAEncryptionPadding KeyPadding = RSAEncryptionPadding.OaepSHA1;

        [Header("Protocol Settings")]
        [SerializeField] private int protocolVersion = 1;

        private RSACryptoServiceProvider localKeyPair;
        private string localPublicKey;

        public int ProtocolVersion => protocolVersion;
        public bool HasKeys => localKeyPair != null;

        protected override Task OnInitialize()
        {
            string storedKey = PlayerPrefs.GetString(PrivateKeyPref, "");
            if (!string.IsNullOrEmpty(storedKey))
            {
                try
                {
                    var rsa = new RSACryptoServiceProvider(RsaKeySizeBits);
                    rsa.FromXmlString(storedKey);
                    localKeyPair = rsa;
                }
                catch (Exception ex)
                {
                    Warn($"Stored key pair could not be loaded ({ex.Message}). Generating a new one.");
                    GenerateNewKeyPair();
                }
            }
            else
            {
                GenerateNewKeyPair();
            }

            localPublicKey = localKeyPair.ToXmlString(false);
            Log("Encryption keys loaded.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Encrypt a message for a specific recipient using their public key.
        /// Hybrid scheme: AES-256-CBC for the payload, RSA-OAEP for the AES key.
        /// Returns null (and logs) on failure.
        /// </summary>
        public byte[] Encrypt(string plaintext, string recipientPublicKey)
        {
            try
            {
                return EncryptForRecipient(plaintext, recipientPublicKey);
            }
            catch (Exception ex)
            {
                Error($"Encryption failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decrypt a message that was encrypted with our public key.
        /// Returns null (and logs) on malformed input or a key mismatch.
        /// </summary>
        public string Decrypt(byte[] cipherData)
        {
            if (localKeyPair == null)
            {
                Error("Decrypt called before the key pair was loaded.");
                return null;
            }

            try
            {
                return DecryptWithKey(cipherData, localKeyPair);
            }
            catch (Exception ex)
            {
                Error($"Decryption failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sign data with our private key so peers can verify it came from us.
        /// </summary>
        public byte[] Sign(byte[] data)
        {
            if (localKeyPair == null || data == null) return null;
            return localKeyPair.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Verify a signature against the sender's public key.
        /// </summary>
        public bool Verify(byte[] data, byte[] signature, string senderPublicKey)
        {
            if (data == null || signature == null || string.IsNullOrEmpty(senderPublicKey))
                return false;

            try
            {
                using (var rsa = new RSACryptoServiceProvider(RsaKeySizeBits))
                {
                    rsa.FromXmlString(senderPublicKey);
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch
            {
                return false;
            }
        }

        public string GetLocalPublicKey() => localPublicKey;

        // ----------------------------------------------------------------
        //  Pure crypto core. Static so edit-mode tests can exercise it
        //  without a scene or a subsystem lifecycle.
        // ----------------------------------------------------------------

        public static byte[] EncryptForRecipient(string plaintext, string recipientPublicKeyXml)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrEmpty(recipientPublicKeyXml))
                throw new ArgumentException("Recipient public key is required.", nameof(recipientPublicKeyXml));

            using (var aes = Aes.Create())
            using (var rsa = new RSACryptoServiceProvider(RsaKeySizeBits))
            {
                aes.KeySize = AesKeySizeBits;
                aes.GenerateKey();
                aes.GenerateIV();
                rsa.FromXmlString(recipientPublicKeyXml);

                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                byte[] ciphertext;
                using (var encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                }

                byte[] encryptedKey = rsa.Encrypt(aes.Key, KeyPadding);
                if (encryptedKey.Length > ushort.MaxValue)
                    throw new CryptographicException("Wrapped key does not fit the wire format.");

                byte[] iv = aes.IV;
                var result = new byte[HeaderLength + encryptedKey.Length + iv.Length + ciphertext.Length];
                int offset = 0;
                result[offset++] = WireVersion;
                result[offset++] = (byte)(encryptedKey.Length & 0xFF);
                result[offset++] = (byte)((encryptedKey.Length >> 8) & 0xFF);
                Buffer.BlockCopy(encryptedKey, 0, result, offset, encryptedKey.Length);
                offset += encryptedKey.Length;
                Buffer.BlockCopy(iv, 0, result, offset, iv.Length);
                offset += iv.Length;
                Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);
                return result;
            }
        }

        public static string DecryptWithKey(byte[] cipherData, RSA privateKey)
        {
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));
            if (cipherData == null || cipherData.Length < HeaderLength + IvLength + 1)
                throw new CryptographicException("Cipher data is too short.");
            if (cipherData[0] != WireVersion)
                throw new CryptographicException($"Unsupported wire version {cipherData[0]}.");

            int keyLen = cipherData[1] | (cipherData[2] << 8);
            int offset = HeaderLength;
            if (keyLen <= 0 || offset + keyLen + IvLength >= cipherData.Length)
                throw new CryptographicException("Cipher data is malformed.");

            var encryptedKey = new byte[keyLen];
            Buffer.BlockCopy(cipherData, offset, encryptedKey, 0, keyLen);
            offset += keyLen;

            var iv = new byte[IvLength];
            Buffer.BlockCopy(cipherData, offset, iv, 0, IvLength);
            offset += IvLength;

            var ciphertext = new byte[cipherData.Length - offset];
            Buffer.BlockCopy(cipherData, offset, ciphertext, 0, ciphertext.Length);

            byte[] aesKey = privateKey.Decrypt(encryptedKey, KeyPadding);

            using (var aes = Aes.Create())
            {
                aes.Key = aesKey;
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] plain = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                    return Encoding.UTF8.GetString(plain);
                }
            }
        }

        private void GenerateNewKeyPair()
        {
            localKeyPair = new RSACryptoServiceProvider(RsaKeySizeBits);
            // Stored in PlayerPrefs, which is app-private storage on Android and iOS.
            // Moving this into the platform keystore is tracked as future hardening.
            PlayerPrefs.SetString(PrivateKeyPref, localKeyPair.ToXmlString(true));
            PlayerPrefs.Save();
            Log("New encryption key pair generated.");
        }

        protected override void OnShutdown()
        {
            localKeyPair?.Dispose();
            localKeyPair = null;
        }
    }
}
