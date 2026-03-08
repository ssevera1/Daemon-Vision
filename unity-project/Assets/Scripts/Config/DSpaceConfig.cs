// DSpaceConfig.cs — Scriptable Object configuration for D-Space
// Centralized configuration that can be tweaked without code changes.

using UnityEngine;
using DaemonVision.HUD;

namespace DaemonVision.Core
{
    [CreateAssetMenu(fileName = "DSpaceConfig", menuName = "DaemonVision/D-Space Config")]
    public class DSpaceConfig : ScriptableObject
    {
        [Header("General")]
        public string AppVersion = "0.1.0-alpha";
        public string DarknetName = "Daemon Vision";
        public int ProtocolVersion = 1;

        [Header("HUD")]
        public float DefaultHUDOpacity = 0.85f;
        public float NameplateDistance = 50f;
        public float NameplateScale = 0.005f;
        public bool ShowThreatIndicators = true;
        public bool ShowMinimap = true;
        public bool ShowCompass = true;
        public bool ShowStatusBar = true;
        public HUDColorScheme ColorScheme;

        [Header("Spatial")]
        public float GPSUpdateInterval = 2f;
        public float AnchorCullDistance = 1000f;
        public int MaxActiveAnchors = 100;

        [Header("Network")]
        public int MeshPort = 7733;
        public int DiscoveryPort = 7734;
        public float HeartbeatInterval = 5f;
        public float PeerTimeout = 30f;
        public int MaxPeers = 20;
        public int MessageHopLimit = 5;

        [Header("Detection")]
        public float PersonDetectionInterval = 0.2f;
        public float MaxDetectionRange = 30f;
        public float ThreatAssessmentInterval = 1f;

        [Header("Social")]
        public int MaxLevel = 200;
        public float BaseXPPerLevel = 100f;
        public float XPScalingFactor = 1.15f;

        [Header("Economy")]
        public long StartingCredits = 100;
        public float TransactionFeePercent = 0.5f;

        [Header("Voice")]
        public float SpatialVoiceRange = 50f;
        public bool PushToTalk = true;
        public bool SpatialAudioEnabled = true;

        [Header("Input")]
        public float GazeDwellTime = 1.5f;
        public string VoiceWakeWord = "daemon";
        public bool RequireWakeWord = true;

        [Header("Debug")]
        public bool EnableDebugOverlay;
        public bool SimulateGPS;
        public double SimulatedLatitude = 37.7749;
        public double SimulatedLongitude = -122.4194;
        public bool SpawnTestOperatives;
        public int TestOperativeCount = 5;
    }
}
