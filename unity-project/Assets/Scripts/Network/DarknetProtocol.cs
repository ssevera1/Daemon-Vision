// DarknetProtocol.cs — Encrypted communication protocol for the darknet
// All D-Space communication is encrypted end-to-end. The Daemon's network
// uses strong encryption to prevent interception and ensure operative privacy.

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

        [Header("Protocol Settings")]
        [SerializeField] private int protocolVersion = 1;

        // Local key pair for encrypted communication
        private RSACryptoServiceProvider localKeyPair;
        private string localPublicKey;

        protected override Task OnInitialize()
        {
            // Generate or load RSA key pair for this operative
            string storedKey = PlayerPrefs.GetString("darknet_private_key", "");
            if (!string.IsNullOrEmpty(storedKey))
            {
                try
                {
                    localKeyPair = new RSACryptoServiceProvider(2048);
                    localKeyPair.FromXmlString(storedKey);
                }
                catch
                {
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
        /// Uses hybrid encryption: AES for data, RSA for AES key.
        /// </summary>
        public byte[] Encrypt(string plaintext, string recipientPublicKey)
        {
            try
            {
                // Generate random AES key
                using (var aes = Aes.Create())
                {
                    aes.GenerateKey();
                    aes.GenerateIV();

                    // Encrypt the message with AES
                    byte[] encrypted;
                    using (var encryptor = aes.CreateEncryptor())
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                        encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                    }

                    // Encrypt the AES key with recipient's RSA public key
                    using (var rsa = new RSACryptoServiceProvider(2048))
                    {
                        rsa.FromXmlString(recipientPublicKey);
                        byte[] encryptedKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);
                        byte[] encryptedIV = rsa.Encrypt(aes.IV, RSAEncryptionPadding.OaepSHA256);

                        // Pack: [keyLen(4)] [key] [ivLen(4)] [iv] [data]
                        byte[] result = new byte[8 + encryptedKey.Length + encryptedIV.Length + encrypted.Length];
                        int offset = 0;

                        BitConverter.GetBytes(encryptedKey.Length).CopyTo(result, offset);
                        offset += 4;
                        encryptedKey.CopyTo(result, offset);
                        offset += encryptedKey.Length;

                        BitConverter.GetBytes(encryptedIV.Length).CopyTo(result, offset);
                        offset += 4;
                        encryptedIV.CopyTo(result, offset);
                        offset += encryptedIV.Length;

                        encrypted.CopyTo(result, offset);

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"Encryption failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decrypt a message sent to us using our private key.
        /// </summary>
        public string Decrypt(byte[] cipherData)
        {
            try
            {
                int offset = 0;

                // Unpack encrypted AES key
                int keyLen = BitConverter.ToInt32(cipherData, offset);
                offset += 4;
                byte[] encryptedKey = new byte[keyLen];
                Array.Copy(cipherData, offset, encryptedKey, 0, keyLen);
                offset += keyLen;

                // Unpack encrypted IV
                int ivLen = BitConverter.ToInt32(cipherData, offset);
                offset += 4;
                byte[] encryptedIV = new byte[ivLen];
                Array.Copy(cipherData, offset, encryptedIV, 0, ivLen);
                offset += ivLen;

                // Encrypted data
                byte[] encryptedData = new byte[cipherData.Length - offset];
                Array.Copy(cipherData, offset, encryptedData, 0, encryptedData.Length);

                // Decrypt AES key and IV with our private key
                byte[] aesKey = localKeyPair.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
                byte[] aesIV = localKeyPair.Decrypt(encryptedIV, RSAEncryptionPadding.OaepSHA256);

                // Decrypt message with AES
                using (var aes = Aes.Create())
                {
                    aes.Key = aesKey;
                    aes.IV = aesIV;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] decrypted = decryptor.TransformFinalBlock(
                            encryptedData, 0, encryptedData.Length);
                        return Encoding.UTF8.GetString(decrypted);
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"Decryption failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sign a message with our private key to prove authenticity.
        /// </summary>
        public byte[] Sign(byte[] data)
        {
            return localKeyPair.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Verify a message signature against a sender's public key.
        /// </summary>
        public bool Verify(byte[] data, byte[] signature, string senderPublicKey)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(2048))
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

        private void GenerateNewKeyPair()
        {
            localKeyPair = new RSACryptoServiceProvider(2048);
            PlayerPrefs.SetString("darknet_private_key", localKeyPair.ToXmlString(true));
            PlayerPrefs.Save();
            Log("New encryption key pair generated.");
        }

        protected override void OnShutdown()
        {
            localKeyPair?.Dispose();
        }
    }
}
