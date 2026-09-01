// VoiceChannelManager.cs — Spatial voice communication for D-Space
// In the Daemon, operatives can voice chat through encrypted channels.
// Voice has spatial properties — nearby operatives are louder, distant ones quieter.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Network;

namespace DaemonVision.Communication
{
    public class VoiceChannelManager : SubsystemBase
    {
        public override string Name => "VoiceChannels";

        [Header("Voice Settings")]
        [SerializeField] private float spatialVoiceRange = 50f;   // meters
        [SerializeField] private float spatialFalloffStart = 10f;
        [SerializeField] private bool enablePushToTalk = true;
        [SerializeField] private bool enableSpatialAudio = true;
        [SerializeField] private float voiceActivationThreshold = 0.02f;

        private MeshNetworkManager meshNetwork;

        private readonly Dictionary<string, VoiceChannel> activeChannels
            = new Dictionary<string, VoiceChannel>();

        private const int MicSampleRate = 16000;      // 16 kHz mono
        private const int MaxSamplesPerPacket = 3200;  // 200 ms; keeps datagrams well under MTU-friendly sizes

        private bool isMicActive;
        private AudioClip micClip;
        private string micDevice;
        private int lastMicPosition;

        public bool IsMicActive => isMicActive;
        public string ActiveChannelId { get; private set; }

        public event Action<string, float[]> OnVoiceDataReceived; // address, audio samples

        protected override Task OnInitialize()
        {
            // Create default voice channels
            activeChannels["proximity"] = new VoiceChannel
            {
                ChannelId = "proximity",
                Name = "Proximity",
                Type = VoiceChannelType.Spatial,
                Range = spatialVoiceRange
            };

            activeChannels["faction"] = new VoiceChannel
            {
                ChannelId = "faction",
                Name = "Faction",
                Type = VoiceChannelType.Faction,
                Range = float.MaxValue
            };

            activeChannels["squad"] = new VoiceChannel
            {
                ChannelId = "squad",
                Name = "Squad",
                Type = VoiceChannelType.Private,
                Range = float.MaxValue
            };

            ActiveChannelId = "proximity";

            // Initialize microphone
            if (Microphone.devices.Length > 0)
            {
                micDevice = Microphone.devices[0];
                Log($"Microphone: {micDevice}");
            }
            else
            {
                Warn("No microphone detected.");
            }

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            meshNetwork = GetSubsystem<MeshNetworkManager>();
        }

        /// <summary>
        /// Start transmitting voice (push-to-talk activate).
        /// </summary>
        public void StartTransmitting()
        {
            if (micDevice == null) return;
            if (isMicActive) return;

            micClip = Microphone.Start(micDevice, true, 1, MicSampleRate);
            lastMicPosition = 0;
            isMicActive = true;
            Log("Voice: Transmitting...");
        }

        /// <summary>
        /// Stop transmitting voice.
        /// </summary>
        public void StopTransmitting()
        {
            if (!isMicActive) return;

            Microphone.End(micDevice);
            isMicActive = false;
            Log("Voice: Stopped.");
        }

        public void JoinChannel(string channelId)
        {
            if (activeChannels.ContainsKey(channelId))
            {
                ActiveChannelId = channelId;
                Log($"Joined voice channel: {channelId}");
            }
        }

        public override void Tick(float deltaTime)
        {
            if (!isMicActive || micClip == null) return;

            // Read only the samples recorded since the last frame. The clip is a
            // one-second ring buffer, so the read position can wrap around.
            int pos = Microphone.GetPosition(micDevice);
            if (pos < 0 || pos == lastMicPosition) return;

            int clipSamples = micClip.samples;
            int count = pos > lastMicPosition
                ? pos - lastMicPosition
                : clipSamples - lastMicPosition + pos;
            if (count <= 0) return;

            // If we fell behind by more than one packet, skip ahead rather than
            // flooding the mesh with stale audio.
            if (count > MaxSamplesPerPacket)
            {
                lastMicPosition = (pos - MaxSamplesPerPacket + clipSamples) % clipSamples;
                count = MaxSamplesPerPacket;
            }

            float[] samples = new float[count];
            if (lastMicPosition + count <= clipSamples)
            {
                micClip.GetData(samples, lastMicPosition);
            }
            else
            {
                int tail = clipSamples - lastMicPosition;
                var tailBuffer = new float[tail];
                micClip.GetData(tailBuffer, lastMicPosition);
                Array.Copy(tailBuffer, 0, samples, 0, tail);

                var headBuffer = new float[count - tail];
                micClip.GetData(headBuffer, 0);
                Array.Copy(headBuffer, 0, samples, tail, headBuffer.Length);
            }
            lastMicPosition = pos;

            // Check voice activation level
            float maxLevel = 0f;
            for (int i = 0; i < samples.Length; i++)
                maxLevel = Mathf.Max(maxLevel, Mathf.Abs(samples[i]));

            if (!enablePushToTalk && maxLevel < voiceActivationThreshold)
                return; // Voice activation: below threshold, don't transmit

            // Compress and send via mesh
            // In production: use Opus codec for compression
            byte[] compressed = CompressAudio(samples);
            meshNetwork?.Broadcast(MeshMessageType.VoiceData,
                Convert.ToBase64String(compressed));
        }

        /// <summary>
        /// Calculate spatial audio volume based on distance between operatives.
        /// </summary>
        public float CalculateSpatialVolume(Vector3 listenerPos, Vector3 speakerPos)
        {
            if (!enableSpatialAudio) return 1f;

            float distance = Vector3.Distance(listenerPos, speakerPos);

            if (distance > spatialVoiceRange) return 0f;
            if (distance < spatialFalloffStart) return 1f;

            return 1f - ((distance - spatialFalloffStart) / (spatialVoiceRange - spatialFalloffStart));
        }

        private byte[] CompressAudio(float[] samples)
        {
            // Simple downsampling compression (replace with Opus in production)
            byte[] result = new byte[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                result[i] = (byte)((samples[i] + 1f) * 127.5f);
            }
            return result;
        }

        protected override void OnShutdown()
        {
            StopTransmitting();
        }
    }

    public class VoiceChannel
    {
        public string ChannelId;
        public string Name;
        public VoiceChannelType Type;
        public float Range;
        public List<string> Members = new List<string>();
    }

    public enum VoiceChannelType
    {
        Spatial,    // Proximity-based with distance attenuation
        Faction,    // All faction members regardless of distance
        Private,    // Invite-only squad channel
        Broadcast   // One-to-many announcement
    }
}
