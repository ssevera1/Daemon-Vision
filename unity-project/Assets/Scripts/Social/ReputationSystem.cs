// ReputationSystem.cs — The Daemon's crowd-sourced reputation system
// Every operative has a 5-star rating with a count of how many people rated them.
// Reputation is THE currency of trust in the darknet — it determines what you can
// access, who will work with you, and your standing in factions.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Social
{
    public class ReputationSystem : SubsystemBase
    {
        public override string Name => "Reputation";

        [Header("Reputation Settings")]
        [SerializeField] private float ratingCooldownHours = 24f; // Can't re-rate same person for 24h
        [SerializeField] private int minimumLevelToRate = 3;
        [SerializeField] private float ratingDecayPerDay = 0.01f; // Slight decay if inactive

        private DarknetIdentityManager identityManager;

        // Local rating history — prevents rating the same person too frequently
        private readonly Dictionary<string, RatingRecord> ratingHistory
            = new Dictionary<string, RatingRecord>();

        // Pending ratings to submit via mesh network
        private readonly Queue<ReputationRating> pendingRatings = new Queue<ReputationRating>();

        public event Action<string, float> OnReputationChanged; // address, new rating

        protected override Task OnInitialize()
        {
            LoadRatingHistory();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
        }

        /// <summary>
        /// Rate another operative. Rating is 1-5 stars.
        /// In the Daemon, reputation is crowd-sourced — the average of all ratings received.
        /// </summary>
        public RatingResult RateOperative(string targetAddress, float stars, string comment = null)
        {
            if (identityManager?.LocalIdentity == null)
                return RatingResult.NotAuthenticated;

            if (targetAddress == identityManager.LocalIdentity.DarknetAddress)
                return RatingResult.CannotRateSelf;

            if (identityManager.LocalIdentity.Level < minimumLevelToRate)
                return RatingResult.LevelTooLow;

            stars = Mathf.Clamp(stars, 1f, 5f);

            // Check cooldown
            if (ratingHistory.TryGetValue(targetAddress, out var record))
            {
                double hoursSinceLastRating =
                    (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - record.Timestamp) / 3600.0;
                if (hoursSinceLastRating < ratingCooldownHours)
                    return RatingResult.CooldownActive;
            }

            // Create rating
            var rating = new ReputationRating
            {
                RaterAddress = identityManager.LocalIdentity.DarknetAddress,
                TargetAddress = targetAddress,
                Stars = stars,
                Comment = comment,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // Apply locally
            var targetIdentity = identityManager.GetIdentity(targetAddress);
            if (targetIdentity != null)
            {
                ApplyRating(targetIdentity, rating);
                OnReputationChanged?.Invoke(targetAddress, targetIdentity.ReputationStars);
            }

            // Queue for mesh broadcast
            pendingRatings.Enqueue(rating);

            // Record in history
            ratingHistory[targetAddress] = new RatingRecord
            {
                TargetAddress = targetAddress,
                LastRating = stars,
                Timestamp = rating.Timestamp
            };

            SaveRatingHistory();
            Log($"Rated {targetAddress[..8]}... : {stars:F1} stars");
            return RatingResult.Success;
        }

        /// <summary>
        /// Apply an incoming rating (from mesh network or local).
        /// Uses weighted running average.
        /// </summary>
        public void ApplyRating(DarknetIdentity target, ReputationRating rating)
        {
            if (target.ReputationCount == 0)
            {
                target.ReputationStars = rating.Stars;
                target.ReputationCount = 1;
            }
            else
            {
                // Weighted average — newer ratings have slightly more weight
                float totalWeight = target.ReputationCount + 1.1f;
                target.ReputationStars =
                    (target.ReputationStars * target.ReputationCount + rating.Stars * 1.1f) / totalWeight;
                target.ReputationCount++;
            }

            target.ReputationStars = Mathf.Clamp(target.ReputationStars, 0f, 5f);
        }

        /// <summary>
        /// Get pending ratings for mesh network broadcast.
        /// </summary>
        public ReputationRating DequeuePendingRating()
        {
            return pendingRatings.Count > 0 ? pendingRatings.Dequeue() : null;
        }

        public bool HasPendingRatings => pendingRatings.Count > 0;

        private void LoadRatingHistory()
        {
            string json = PlayerPrefs.GetString("reputation_history", "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<RatingHistoryWrapper>(json);
                    if (wrapper?.Records != null)
                    {
                        foreach (var record in wrapper.Records)
                            ratingHistory[record.TargetAddress] = record;
                    }
                }
                catch { }
            }
        }

        private void SaveRatingHistory()
        {
            var wrapper = new RatingHistoryWrapper
            {
                Records = new List<RatingRecord>(ratingHistory.Values)
            };
            PlayerPrefs.SetString("reputation_history", JsonUtility.ToJson(wrapper));
        }
    }

    [Serializable]
    public class ReputationRating
    {
        public string RaterAddress;
        public string TargetAddress;
        public float Stars;
        public string Comment;
        public long Timestamp;
    }

    [Serializable]
    public class RatingRecord
    {
        public string TargetAddress;
        public float LastRating;
        public long Timestamp;
    }

    [Serializable]
    public class RatingHistoryWrapper
    {
        public List<RatingRecord> Records;
    }

    public enum RatingResult
    {
        Success,
        NotAuthenticated,
        CannotRateSelf,
        LevelTooLow,
        CooldownActive,
        NetworkError
    }
}
