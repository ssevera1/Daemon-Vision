// LevelProgression.cs — The Daemon's 200-level progression system
// Operatives advance from Level 1 to 200 by completing quests, gaining reputation,
// and contributing to the darknet. Higher levels unlock new abilities, access to
// powerful D-Space tools, and leadership positions.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Social
{
    public class LevelProgression : SubsystemBase
    {
        public override string Name => "Leveling";

        public const int MaxLevel = 200;

        [Header("XP Settings")]
        [SerializeField] private float baseXPPerLevel = 100f;
        [SerializeField] private float xpScalingFactor = 1.15f; // Each level requires 15% more XP
        [SerializeField] private bool enableLevelNotifications = true;

        private DarknetIdentityManager identityManager;

        private float currentXP;
        private float xpToNextLevel;

        public float CurrentXP => currentXP;
        public float XPToNextLevel => xpToNextLevel;
        public float XPProgress => xpToNextLevel > 0 ? currentXP / xpToNextLevel : 0f;

        public event Action<int> OnLevelUp;
        public event Action<float> OnXPGained;

        protected override Task OnInitialize()
        {
            currentXP = PlayerPrefs.GetFloat("darknet_xp", 0f);
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            RecalculateXPThreshold();
        }

        /// <summary>
        /// Award XP from various sources: quest completion, reputation, contributions.
        /// </summary>
        public void AwardXP(float amount, string source)
        {
            if (amount <= 0) return;
            if (identityManager?.LocalIdentity == null) return;

            var identity = identityManager.LocalIdentity;
            if (identity.Level >= MaxLevel)
            {
                Log("Max level reached.");
                return;
            }

            currentXP += amount;
            OnXPGained?.Invoke(amount);
            Log($"+{amount:F0} XP from {source}. Progress: {currentXP:F0}/{xpToNextLevel:F0}");

            // Check for level up(s)
            while (currentXP >= xpToNextLevel && identity.Level < MaxLevel)
            {
                currentXP -= xpToNextLevel;
                identity.Level++;
                RecalculateXPThreshold();

                Log($"=== LEVEL UP! Now Level {identity.Level} ===");
                OnLevelUp?.Invoke(identity.Level);

                // Check for milestone unlocks
                CheckMilestoneUnlocks(identity.Level);
            }

            identityManager.SaveLocalIdentity(identity);
            PlayerPrefs.SetFloat("darknet_xp", currentXP);
        }

        /// <summary>
        /// Calculate XP required for the next level.
        /// Uses exponential scaling — early levels are fast, later levels are a grind.
        /// </summary>
        public float CalculateXPForLevel(int level)
        {
            return baseXPPerLevel * Mathf.Pow(xpScalingFactor, level - 1);
        }

        private void RecalculateXPThreshold()
        {
            int level = identityManager?.LocalIdentity?.Level ?? 1;
            xpToNextLevel = CalculateXPForLevel(level);
        }

        /// <summary>
        /// XP rewards for different activities (based on the Daemon's reward structure).
        /// </summary>
        public static class XPRewards
        {
            public const float QuestComplete = 50f;
            public const float QuestCompleteHard = 150f;
            public const float QuestCompleteLegendary = 500f;
            public const float RatingGiven = 5f;
            public const float RatingReceived = 2f;
            public const float PositiveRating = 10f;       // Received 4+ star rating
            public const float AnchorCreated = 10f;
            public const float PeerHelped = 20f;
            public const float FactionContribution = 30f;
            public const float FirstLogin = 25f;
            public const float DailyActive = 15f;
            public const float MeshNodeHosted = 5f;         // Per hour
            public const float ThreatReported = 25f;
            public const float CommunityProject = 100f;
        }

        private void CheckMilestoneUnlocks(int level)
        {
            // Key milestone levels from the Daemon
            switch (level)
            {
                case 5:
                    Log("Milestone: Faction membership unlocked.");
                    break;
                case 10:
                    Log("Milestone: Quest creation unlocked.");
                    break;
                case 20:
                    Log("Milestone: Advanced D-Space constructs unlocked.");
                    break;
                case 30:
                    Log("Milestone: AutoM8 interface unlocked.");
                    break;
                case 50:
                    Log("Milestone: Autonomous systems command unlocked.");
                    break;
                case 75:
                    Log("Milestone: Regional leadership eligible.");
                    break;
                case 100:
                    Log("Milestone: Centurion status. Full darknet access.");
                    break;
                case 150:
                    Log("Milestone: Elder operative. Governance participation.");
                    break;
                case 200:
                    Log("Milestone: MAX LEVEL. Sobol's Legacy achieved.");
                    break;
            }
        }

        protected override void OnShutdown()
        {
            PlayerPrefs.SetFloat("darknet_xp", currentXP);
        }
    }
}
