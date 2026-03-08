// DarknetIdentityManager.cs — Manages darknet operative identities
// In the Daemon, every operative has a callsign, level, class, faction, and reputation
// visible as floating call-outs above their heads in D-Space

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Social;

namespace DaemonVision.Identity
{
    public class DarknetIdentityManager : SubsystemBase
    {
        public override string Name => "Identity";

        /// <summary>
        /// The local operative's identity — "you" in D-Space.
        /// </summary>
        public DarknetIdentity LocalIdentity { get; private set; }

        /// <summary>
        /// All known operative identities keyed by their darknet address (public key hash).
        /// </summary>
        private readonly Dictionary<string, DarknetIdentity> knownIdentities
            = new Dictionary<string, DarknetIdentity>();

        public event Action<DarknetIdentity> OnIdentityDiscovered;
        public event Action<DarknetIdentity> OnIdentityUpdated;
        public event Action<string> OnIdentityLost;

        protected override Task OnInitialize()
        {
            // Load or create local identity
            LocalIdentity = LoadLocalIdentity() ?? CreateNewIdentity();
            knownIdentities[LocalIdentity.DarknetAddress] = LocalIdentity;
            Log($"Local identity: {LocalIdentity.Callsign} [{LocalIdentity.DarknetAddress[..8]}...]");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Register a discovered operative from the mesh network.
        /// </summary>
        public void RegisterPeerIdentity(DarknetIdentity identity)
        {
            if (identity == null || string.IsNullOrEmpty(identity.DarknetAddress))
                return;

            bool isNew = !knownIdentities.ContainsKey(identity.DarknetAddress);
            knownIdentities[identity.DarknetAddress] = identity;

            if (isNew)
            {
                Log($"Discovered operative: {identity.Callsign} (Lv.{identity.Level})");
                OnIdentityDiscovered?.Invoke(identity);
            }
            else
            {
                OnIdentityUpdated?.Invoke(identity);
            }
        }

        public void RemovePeerIdentity(string darknetAddress)
        {
            if (knownIdentities.Remove(darknetAddress))
            {
                OnIdentityLost?.Invoke(darknetAddress);
            }
        }

        public DarknetIdentity GetIdentity(string darknetAddress)
        {
            knownIdentities.TryGetValue(darknetAddress, out var identity);
            return identity;
        }

        public IEnumerable<DarknetIdentity> GetAllIdentities() => knownIdentities.Values;

        public int KnownOperativeCount => knownIdentities.Count;

        private DarknetIdentity LoadLocalIdentity()
        {
            string json = PlayerPrefs.GetString("darknet_identity", null);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<DarknetIdentity>(json);
            }
            catch
            {
                return null;
            }
        }

        private DarknetIdentity CreateNewIdentity()
        {
            var identity = new DarknetIdentity
            {
                DarknetAddress = GenerateDarknetAddress(),
                Callsign = CallsignGenerator.Generate(),
                Level = 1,
                DarknetClass = DarknetClass.Unassigned,
                Faction = "Independent",
                ReputationStars = 0f,
                ReputationCount = 0,
                CreatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            SaveLocalIdentity(identity);
            return identity;
        }

        public void SaveLocalIdentity(DarknetIdentity identity)
        {
            PlayerPrefs.SetString("darknet_identity", JsonUtility.ToJson(identity));
            PlayerPrefs.Save();
        }

        private string GenerateDarknetAddress()
        {
            // Generate a pseudo-random darknet address (in production, use Ed25519 public key)
            byte[] bytes = new byte[32];
            var rng = new System.Security.Cryptography.RNGCryptoServiceProvider();
            rng.GetBytes(bytes);
            rng.Dispose();
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        protected override void OnShutdown()
        {
            if (LocalIdentity != null)
                SaveLocalIdentity(LocalIdentity);
        }
    }

    /// <summary>
    /// A darknet operative's identity — the data displayed in their floating call-out.
    /// Matches the Daemon's format: Callsign, Level (1-200), Class, Faction, Reputation (5 stars).
    /// </summary>
    [Serializable]
    public class DarknetIdentity
    {
        public string DarknetAddress;       // Public key hash — unique network identifier
        public string Callsign;             // Display name (e.g., "Loki", "Unnamed_7")
        public int Level;                   // 1–200, Sobol's leveling system
        public DarknetClass DarknetClass;   // Fighter, Sorcerer, Shaman, etc.
        public string Faction;              // Order of Merritt, Dark Rose, etc.
        public float ReputationStars;       // 0.0–5.0 crowd-sourced rating
        public int ReputationCount;         // Number of ratings received
        public string Title;                // Optional earned title
        public long CreatedTimestamp;
        public string AvatarId;             // Optional custom avatar/icon

        /// <summary>
        /// Threat level assigned by local ThreatAssessment system.
        /// Not transmitted — computed locally based on behavior and context.
        /// </summary>
        [NonSerialized] public ThreatLevel LocalThreatLevel;

        /// <summary>
        /// World position where this operative was last detected.
        /// </summary>
        [NonSerialized] public Vector3 LastKnownPosition;

        /// <summary>
        /// Time since last network heartbeat from this operative.
        /// </summary>
        [NonSerialized] public float TimeSinceLastSeen;

        public string GetDisplayString()
        {
            string stars = new string('★', Mathf.FloorToInt(ReputationStars))
                         + (ReputationStars % 1 >= 0.5f ? "½" : "");
            return $"{Callsign}\nLv.{Level} {DarknetClass}\n{stars} ({ReputationCount})\n{Faction}";
        }
    }

    public enum DarknetClass
    {
        Unassigned,
        Fighter,    // Combat specialists
        Sorcerer,   // Hackers / tech specialists
        Shaman,     // Healers / community builders
        Scout,      // Reconnaissance / intelligence
        Fabricator,  // Builders / makers / engineers
        Journalist,  // Information / media
        Rogue       // Covert operations
    }

    public enum ThreatLevel
    {
        None,
        Low,
        Moderate,
        High,
        Critical
    }

    /// <summary>
    /// Generates darknet callsigns in the style of the Daemon novels.
    /// </summary>
    public static class CallsignGenerator
    {
        private static readonly string[] Prefixes =
        {
            "Shadow", "Dark", "Ghost", "Null", "Void", "Cipher", "Echo", "Daemon",
            "Flux", "Nexus", "Proxy", "Vector", "Binary", "Helix", "Rune", "Spark",
            "Vortex", "Zenith", "Omega", "Prism", "Storm", "Blade", "Forge", "Wraith",
            "Onyx", "Neon", "Pulse", "Rift", "Surge", "Thorn", "Arc", "Chrome"
        };

        private static readonly string[] Suffixes =
        {
            "Runner", "Walker", "Weaver", "Smith", "Hawk", "Wolf", "Fox", "Raven",
            "Strike", "Lance", "Shield", "Guard", "Watch", "Eye", "Hand", "Mind",
            "Core", "Node", "Link", "Gate", "Key", "Lock", "Wire", "Net",
            "Drift", "Flow", "Wave", "Spark", "Flash", "Burn", "Frost", "Shade"
        };

        public static string Generate()
        {
            var prefix = Prefixes[UnityEngine.Random.Range(0, Prefixes.Length)];
            var suffix = Suffixes[UnityEngine.Random.Range(0, Suffixes.Length)];
            var number = UnityEngine.Random.Range(1, 999);
            return $"{prefix}{suffix}_{number}";
        }
    }
}
