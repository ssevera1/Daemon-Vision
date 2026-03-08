// FactionManager.cs — Darknet faction management
// In the Daemon, operatives belong to factions like the Order of Merritt,
// Merittorious Raiders, Dark Rose, and GamerZ. Factions control territory,
// have hierarchy, and provide level-gated capabilities.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Social
{
    public class FactionManager : SubsystemBase
    {
        public override string Name => "Factions";

        private readonly Dictionary<string, Faction> registeredFactions
            = new Dictionary<string, Faction>();

        private DarknetIdentityManager identityManager;

        public event Action<string, string> OnFactionJoined;    // address, factionId
        public event Action<string, string> OnFactionLeft;
        public event Action<Faction> OnFactionRegistered;

        protected override Task OnInitialize()
        {
            // Register default factions from the Daemon universe
            RegisterDefaultFactions();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
        }

        public Faction GetFaction(string factionId)
        {
            registeredFactions.TryGetValue(factionId, out var faction);
            return faction;
        }

        public IEnumerable<Faction> GetAllFactions() => registeredFactions.Values;

        public void RegisterFaction(Faction faction)
        {
            registeredFactions[faction.Id] = faction;
            OnFactionRegistered?.Invoke(faction);
            Log($"Faction registered: {faction.Name}");
        }

        public FactionJoinResult JoinFaction(string factionId)
        {
            if (identityManager?.LocalIdentity == null)
                return FactionJoinResult.NotAuthenticated;

            if (!registeredFactions.TryGetValue(factionId, out var faction))
                return FactionJoinResult.FactionNotFound;

            var identity = identityManager.LocalIdentity;

            if (identity.Level < faction.MinimumLevel)
                return FactionJoinResult.LevelTooLow;

            if (faction.RequiredReputation > 0 && identity.ReputationStars < faction.RequiredReputation)
                return FactionJoinResult.ReputationTooLow;

            if (faction.RequiresInvitation && !faction.PendingInvites.Contains(identity.DarknetAddress))
                return FactionJoinResult.InviteRequired;

            identity.Faction = faction.Name;
            faction.MemberAddresses.Add(identity.DarknetAddress);
            identityManager.SaveLocalIdentity(identity);

            OnFactionJoined?.Invoke(identity.DarknetAddress, factionId);
            Log($"Joined faction: {faction.Name}");
            return FactionJoinResult.Success;
        }

        public void LeaveFaction()
        {
            if (identityManager?.LocalIdentity == null) return;

            var identity = identityManager.LocalIdentity;
            string oldFaction = identity.Faction;

            foreach (var faction in registeredFactions.Values)
            {
                faction.MemberAddresses.Remove(identity.DarknetAddress);
            }

            identity.Faction = "Independent";
            identityManager.SaveLocalIdentity(identity);

            OnFactionLeft?.Invoke(identity.DarknetAddress, oldFaction);
            Log("Left faction. Now Independent.");
        }

        public bool IsInFaction(string darknetAddress, string factionId)
        {
            if (registeredFactions.TryGetValue(factionId, out var faction))
                return faction.MemberAddresses.Contains(darknetAddress);
            return false;
        }

        private void RegisterDefaultFactions()
        {
            RegisterFaction(new Faction
            {
                Id = "order_of_merritt",
                Name = "Order of Merritt",
                Description = "Builders and sustainers of the new distributed economy. Focus on community resilience, local manufacturing, and sustainable technology.",
                Color = new Color(0.2f, 0.6f, 1f),
                MinimumLevel = 5,
                RequiredReputation = 2.0f,
                RequiresInvitation = false
            });

            RegisterFaction(new Faction
            {
                Id = "merittorious_raiders",
                Name = "Merittorious Raiders",
                Description = "Rapid response and defense operatives. Protect darknet communities and respond to threats against the network.",
                Color = new Color(1f, 0.4f, 0.1f),
                MinimumLevel = 10,
                RequiredReputation = 3.0f,
                RequiresInvitation = false
            });

            RegisterFaction(new Faction
            {
                Id = "dark_rose",
                Name = "Dark Rose",
                Description = "Intelligence and covert operations. Gather information, run counter-surveillance, and protect operative identities.",
                Color = new Color(0.8f, 0.1f, 0.3f),
                MinimumLevel = 15,
                RequiredReputation = 3.5f,
                RequiresInvitation = true
            });

            RegisterFaction(new Faction
            {
                Id = "gamerz",
                Name = "GamerZ",
                Description = "Chaotic operatives motivated by entertainment and personal gain. Low barrier to entry, high variance in quality.",
                Color = new Color(0.5f, 1f, 0.2f),
                MinimumLevel = 1,
                RequiredReputation = 0f,
                RequiresInvitation = false
            });

            RegisterFaction(new Faction
            {
                Id = "independent",
                Name = "Independent",
                Description = "Unaffiliated operatives. Freedom of action, but no faction support or resources.",
                Color = new Color(0.7f, 0.7f, 0.7f),
                MinimumLevel = 0,
                RequiredReputation = 0f,
                RequiresInvitation = false
            });
        }
    }

    [Serializable]
    public class Faction
    {
        public string Id;
        public string Name;
        public string Description;
        public Color Color;
        public int MinimumLevel;
        public float RequiredReputation;
        public bool RequiresInvitation;
        public List<string> MemberAddresses = new List<string>();
        public List<string> PendingInvites = new List<string>();
    }

    public enum FactionJoinResult
    {
        Success,
        NotAuthenticated,
        FactionNotFound,
        LevelTooLow,
        ReputationTooLow,
        InviteRequired,
        AlreadyMember
    }
}
