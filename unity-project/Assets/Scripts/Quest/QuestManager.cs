// QuestManager.cs — The Daemon's quest thread system
// Quests in the Daemon are real-world tasks presented as MMORPG-style missions.
// They appear as glowing AR quest threads in D-Space, guiding operatives
// to objectives. Quests can be public (community) or private (faction-specific).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;
using DaemonVision.Social;

namespace DaemonVision.Quest
{
    public class QuestManager : SubsystemBase
    {
        public override string Name => "Quests";

        [Header("Quest Settings")]
        [SerializeField] private int maxActiveQuests = 10;
        [SerializeField] private float questCheckRadius = 500f; // meters for nearby quests

        private DarknetIdentityManager identityManager;
        private LevelProgression levelSystem;

        private readonly Dictionary<string, QuestData> allQuests = new Dictionary<string, QuestData>();
        private readonly Dictionary<string, QuestData> activeQuests = new Dictionary<string, QuestData>();
        private readonly HashSet<string> completedQuestIds = new HashSet<string>();

        public int ActiveQuestCount => activeQuests.Count;

        public event Action<QuestData> OnQuestAvailable;
        public event Action<QuestData> OnQuestAccepted;
        public event Action<string> OnQuestCompleted;
        public event Action<string> OnQuestAbandoned;
        public event Action<string, int> OnObjectiveUpdated; // questId, objectiveIndex

        protected override Task OnInitialize()
        {
            LoadCompletedQuests();
            RegisterStarterQuests();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            levelSystem = GetSubsystem<LevelProgression>();
        }

        /// <summary>
        /// Register a new quest in D-Space. Quests can come from the network,
        /// faction leaders, or the system itself.
        /// </summary>
        public void RegisterQuest(QuestData quest)
        {
            if (allQuests.ContainsKey(quest.QuestId)) return;

            allQuests[quest.QuestId] = quest;
            OnQuestAvailable?.Invoke(quest);
            Log($"Quest available: \"{quest.Title}\" (Lv.{quest.MinimumLevel}+)");
        }

        public AcceptResult AcceptQuest(string questId)
        {
            if (!allQuests.TryGetValue(questId, out var quest))
                return AcceptResult.QuestNotFound;

            if (activeQuests.Count >= maxActiveQuests)
                return AcceptResult.QuestLogFull;

            if (completedQuestIds.Contains(questId) && !quest.Repeatable)
                return AcceptResult.AlreadyCompleted;

            if (activeQuests.ContainsKey(questId))
                return AcceptResult.AlreadyActive;

            var identity = identityManager?.LocalIdentity;
            if (identity == null) return AcceptResult.NotAuthenticated;

            if (identity.Level < quest.MinimumLevel)
                return AcceptResult.LevelTooLow;

            if (!string.IsNullOrEmpty(quest.RequiredFaction) && identity.Faction != quest.RequiredFaction)
                return AcceptResult.WrongFaction;

            quest.State = QuestState.Active;
            quest.AcceptedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            activeQuests[questId] = quest;

            OnQuestAccepted?.Invoke(quest);
            Log($"Quest accepted: \"{quest.Title}\"");
            return AcceptResult.Success;
        }

        /// <summary>
        /// Update quest objective progress. Called when an operative performs
        /// an action that advances a quest (reaching a location, completing a task, etc.).
        /// </summary>
        public void UpdateObjective(string questId, int objectiveIndex, int progress)
        {
            if (!activeQuests.TryGetValue(questId, out var quest)) return;
            if (objectiveIndex < 0 || objectiveIndex >= quest.Objectives.Count) return;

            var objective = quest.Objectives[objectiveIndex];
            objective.CurrentProgress = Mathf.Min(progress, objective.RequiredProgress);

            OnObjectiveUpdated?.Invoke(questId, objectiveIndex);

            // Check if objective is complete
            if (objective.IsComplete)
            {
                Log($"Objective complete: {objective.Description}");

                // Check if all objectives are complete
                if (quest.Objectives.All(o => o.IsComplete))
                {
                    CompleteQuest(questId);
                }
            }
        }

        /// <summary>
        /// Check if the operative is near a quest objective (GPS-based proximity check).
        /// </summary>
        public void CheckProximityObjectives(Vector3 playerPosition)
        {
            foreach (var quest in activeQuests.Values)
            {
                var currentObj = quest.GetCurrentObjective();
                if (currentObj?.TargetPosition == null) continue;
                if (currentObj.Type != ObjectiveType.ReachLocation) continue;

                float dist = Vector3.Distance(playerPosition, currentObj.TargetPosition.Value);
                if (dist <= currentObj.ProximityRadius)
                {
                    UpdateObjective(quest.QuestId,
                        quest.Objectives.IndexOf(currentObj),
                        currentObj.RequiredProgress);
                }
            }
        }

        public void AbandonQuest(string questId)
        {
            if (activeQuests.TryGetValue(questId, out var quest))
            {
                quest.State = QuestState.Available;
                activeQuests.Remove(questId);
                OnQuestAbandoned?.Invoke(questId);
                Log($"Quest abandoned: \"{quest.Title}\"");
            }
        }

        public QuestData GetQuest(string questId)
        {
            activeQuests.TryGetValue(questId, out var quest);
            if (quest == null)
                allQuests.TryGetValue(questId, out quest);
            return quest;
        }

        public IEnumerable<QuestData> GetActiveQuests() => activeQuests.Values;
        public IEnumerable<QuestData> GetAvailableQuests() => allQuests.Values.Where(
            q => q.State == QuestState.Available && !completedQuestIds.Contains(q.QuestId));

        private void CompleteQuest(string questId)
        {
            if (!activeQuests.TryGetValue(questId, out var quest)) return;

            quest.State = QuestState.Completed;
            activeQuests.Remove(questId);
            completedQuestIds.Add(questId);

            // Award rewards
            if (levelSystem != null)
            {
                levelSystem.AwardXP(quest.XPReward, $"Quest: {quest.Title}");
            }

            SaveCompletedQuests();
            OnQuestCompleted?.Invoke(questId);
            Log($"=== QUEST COMPLETE: \"{quest.Title}\" (+{quest.XPReward} XP, +{quest.CreditReward} credits) ===");
        }

        private void RegisterStarterQuests()
        {
            // Tutorial / starter quests inspired by the Daemon's onboarding
            RegisterQuest(new QuestData
            {
                QuestId = "starter_01",
                Title = "Awaken",
                Description = "Complete your D-Space calibration and authenticate with the darknet.",
                MinimumLevel = 1,
                Difficulty = QuestDifficulty.Tutorial,
                XPReward = 50,
                CreditReward = 100,
                Objectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Description = "Complete biometric authentication",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    },
                    new QuestObjective
                    {
                        Description = "Calibrate your HUD display",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    }
                }
            });

            RegisterQuest(new QuestData
            {
                QuestId = "starter_02",
                Title = "First Steps in D-Space",
                Description = "Explore your surroundings and discover D-Space anchors in your area.",
                MinimumLevel = 1,
                Difficulty = QuestDifficulty.Easy,
                XPReward = 75,
                CreditReward = 50,
                Objectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Description = "Walk 100 meters with D-Space active",
                        Type = ObjectiveType.Distance,
                        RequiredProgress = 100
                    },
                    new QuestObjective
                    {
                        Description = "Discover 3 D-Space anchors",
                        Type = ObjectiveType.Collect,
                        RequiredProgress = 3
                    }
                }
            });

            RegisterQuest(new QuestData
            {
                QuestId = "starter_03",
                Title = "Join the Network",
                Description = "Connect with another darknet operative via mesh networking.",
                MinimumLevel = 2,
                Difficulty = QuestDifficulty.Easy,
                XPReward = 100,
                CreditReward = 75,
                Objectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Description = "Detect another operative in D-Space",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    },
                    new QuestObjective
                    {
                        Description = "Exchange darknet credentials",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    }
                }
            });

            RegisterQuest(new QuestData
            {
                QuestId = "starter_04",
                Title = "Choose Your Path",
                Description = "Select your darknet class and begin your specialization.",
                MinimumLevel = 3,
                Difficulty = QuestDifficulty.Easy,
                XPReward = 150,
                CreditReward = 100,
                Objectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Description = "Review all 7 darknet classes",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    },
                    new QuestObjective
                    {
                        Description = "Choose your class",
                        Type = ObjectiveType.Action,
                        RequiredProgress = 1
                    }
                }
            });
        }

        private void LoadCompletedQuests()
        {
            string data = PlayerPrefs.GetString("completed_quests", "");
            if (!string.IsNullOrEmpty(data))
            {
                foreach (var id in data.Split(','))
                    if (!string.IsNullOrEmpty(id))
                        completedQuestIds.Add(id);
            }
        }

        private void SaveCompletedQuests()
        {
            PlayerPrefs.SetString("completed_quests", string.Join(",", completedQuestIds));
        }
    }

    [Serializable]
    public class QuestData
    {
        public string QuestId;
        public string Title;
        public string Description;
        public int MinimumLevel;
        public string RequiredFaction;
        public QuestDifficulty Difficulty;
        public QuestState State = QuestState.Available;
        public float XPReward;
        public long CreditReward;
        public bool Repeatable;
        public long AcceptedTimestamp;
        public List<QuestObjective> Objectives = new List<QuestObjective>();
        public string CreatorAddress;   // Who created this quest

        public QuestObjective GetCurrentObjective()
        {
            return Objectives.FirstOrDefault(o => !o.IsComplete);
        }
    }

    [Serializable]
    public class QuestObjective
    {
        public string Description;
        public ObjectiveType Type;
        public int RequiredProgress;
        public int CurrentProgress;
        public Vector3? TargetPosition;
        public float ProximityRadius = 5f;  // meters for location objectives

        public bool IsComplete => CurrentProgress >= RequiredProgress;
    }

    public enum QuestState { Available, Active, Completed, Failed }
    public enum QuestDifficulty { Tutorial, Easy, Medium, Hard, Legendary }
    public enum ObjectiveType { Action, ReachLocation, Collect, Distance, Interact, Defend, Craft }
    public enum AcceptResult { Success, QuestNotFound, QuestLogFull, AlreadyCompleted, AlreadyActive, NotAuthenticated, LevelTooLow, WrongFaction }
}
