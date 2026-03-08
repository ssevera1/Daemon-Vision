// QuestDatabase.cs — Persistent quest storage for D-Space
// Stores quest definitions received from the mesh network, tracks per-operative
// quest progress, and maintains completion history. All data persists through
// DataPersistence so it survives app restarts.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Quest;

namespace DaemonVision.Data
{
    /// <summary>
    /// Persistent storage for all quest data in D-Space. Quest definitions arrive
    /// from the mesh network or are generated locally; progress is tracked per-
    /// operative and persisted through DataPersistence.
    /// </summary>
    public class QuestDatabase : SubsystemBase
    {
        public override string Name => "QuestDatabase";

        [Header("Database Settings")]
        [SerializeField] private int maxStoredQuests = 500;
        [SerializeField] private int maxCompletionHistory = 1000;
        [SerializeField] private float persistIntervalSeconds = 120f;

        private DataPersistence persistence;

        // All known quest definitions: questId -> QuestRecord
        private readonly Dictionary<string, QuestRecord> questRecords
            = new Dictionary<string, QuestRecord>();

        // Completion history: questId -> list of completion records
        private readonly Dictionary<string, List<CompletionRecord>> completionHistory
            = new Dictionary<string, List<CompletionRecord>>();

        // Quest progress snapshots for persistence: questId -> progress data
        private readonly Dictionary<string, QuestProgressData> progressStore
            = new Dictionary<string, QuestProgressData>();

        private float persistTimer;
        private bool isDirty;

        private const string QuestsKey = "quests/quest_definitions";
        private const string ProgressKey = "quests/quest_progress";
        private const string HistoryKey = "quests/completion_history";

        public int QuestCount => questRecords.Count;
        public int CompletedCount => completionHistory.Values.Sum(h => h.Count);

        public event Action<QuestRecord> OnQuestStored;
        public event Action<string> OnQuestRemoved;
        public event Action<string, QuestProgressData> OnProgressSaved;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            persistence = GetSubsystem<DataPersistence>();

            if (persistence != null)
            {
                LoadFromDisk();
            }
            else
            {
                Warn("DataPersistence not available. Quest data will not be persisted.");
            }
        }

        public override void Tick(float deltaTime)
        {
            persistTimer += deltaTime;

            if (isDirty && persistTimer >= persistIntervalSeconds)
            {
                persistTimer = 0f;
                PersistToDisk();
            }
        }

        protected override void OnShutdown()
        {
            PersistToDisk();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Quest Definition Storage
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Store a quest definition. If the quest already exists, updates it
        /// only if the new version has a newer timestamp.
        /// </summary>
        public void StoreQuest(QuestData questData)
        {
            if (questData == null || string.IsNullOrEmpty(questData.QuestId))
            {
                Warn("Attempted to store quest with null/empty ID.");
                return;
            }

            // Check if we already have a newer version
            if (questRecords.TryGetValue(questData.QuestId, out var existing))
            {
                if (existing.ReceivedTimestamp > 0 &&
                    questData.AcceptedTimestamp <= existing.QuestData.AcceptedTimestamp)
                {
                    return; // Existing is newer
                }
            }

            // Enforce capacity limit
            if (!questRecords.ContainsKey(questData.QuestId) && questRecords.Count >= maxStoredQuests)
            {
                EvictOldestQuest();
            }

            var record = new QuestRecord
            {
                QuestData = questData,
                ReceivedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Source = string.IsNullOrEmpty(questData.CreatorAddress) ? "system" : "network"
            };

            questRecords[questData.QuestId] = record;
            OnQuestStored?.Invoke(record);
            MarkDirty();
        }

        /// <summary>
        /// Remove a quest definition from the database.
        /// </summary>
        public bool RemoveQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return false;

            if (questRecords.Remove(questId))
            {
                progressStore.Remove(questId);
                OnQuestRemoved?.Invoke(questId);
                MarkDirty();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get a specific quest by ID.
        /// </summary>
        public QuestData GetQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return questRecords.TryGetValue(questId, out var record) ? record.QuestData : null;
        }

        /// <summary>
        /// Get the full record (including metadata) for a quest.
        /// </summary>
        public QuestRecord GetQuestRecord(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return questRecords.TryGetValue(questId, out var record) ? record : null;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Quest Queries
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Get all quests the operative can currently accept (available, not completed
        /// unless repeatable, meets level/faction requirements).
        /// </summary>
        public List<QuestData> GetAvailableQuests(int operativeLevel = 1, string faction = null)
        {
            var results = new List<QuestData>();

            foreach (var record in questRecords.Values)
            {
                var quest = record.QuestData;

                if (quest.State != QuestState.Available)
                    continue;

                // Level gate
                if (quest.MinimumLevel > operativeLevel)
                    continue;

                // Faction gate
                if (!string.IsNullOrEmpty(quest.RequiredFaction) &&
                    !string.IsNullOrEmpty(faction) &&
                    quest.RequiredFaction != faction)
                    continue;

                // Already completed (non-repeatable)
                if (!quest.Repeatable && IsQuestCompleted(quest.QuestId))
                    continue;

                results.Add(quest);
            }

            return results;
        }

        /// <summary>
        /// Get all quests currently marked as active.
        /// </summary>
        public List<QuestData> GetActiveQuests()
        {
            return questRecords.Values
                .Where(r => r.QuestData.State == QuestState.Active)
                .Select(r => r.QuestData)
                .ToList();
        }

        /// <summary>
        /// Get all completed quest IDs with their completion records.
        /// </summary>
        public List<CompletionRecord> GetCompletedQuests()
        {
            var results = new List<CompletionRecord>();
            foreach (var history in completionHistory.Values)
            {
                results.AddRange(history);
            }
            return results.OrderByDescending(c => c.CompletedTimestamp).ToList();
        }

        /// <summary>
        /// Check if a quest has been completed at least once.
        /// </summary>
        public bool IsQuestCompleted(string questId)
        {
            return completionHistory.ContainsKey(questId) &&
                   completionHistory[questId].Count > 0;
        }

        /// <summary>
        /// Get all quests from a specific creator (darknet address).
        /// </summary>
        public List<QuestData> GetQuestsByCreator(string creatorAddress)
        {
            if (string.IsNullOrEmpty(creatorAddress)) return new List<QuestData>();

            return questRecords.Values
                .Where(r => r.QuestData.CreatorAddress == creatorAddress)
                .Select(r => r.QuestData)
                .ToList();
        }

        /// <summary>
        /// Get all quests matching a difficulty filter.
        /// </summary>
        public List<QuestData> GetQuestsByDifficulty(QuestDifficulty difficulty)
        {
            return questRecords.Values
                .Where(r => r.QuestData.Difficulty == difficulty)
                .Select(r => r.QuestData)
                .ToList();
        }

        /// <summary>
        /// Get all stored quest definitions.
        /// </summary>
        public List<QuestData> GetAllQuests()
        {
            return questRecords.Values.Select(r => r.QuestData).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Progress Tracking
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Save progress for an active quest. Called periodically by the QuestManager
        /// to ensure progress survives app restarts.
        /// </summary>
        public void SaveProgress(string questId, QuestProgressData progress)
        {
            if (string.IsNullOrEmpty(questId) || progress == null) return;

            progress.LastUpdatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            progressStore[questId] = progress;

            OnProgressSaved?.Invoke(questId, progress);
            MarkDirty();
        }

        /// <summary>
        /// Load saved progress for a quest. Returns null if no progress exists.
        /// </summary>
        public QuestProgressData LoadProgress(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return progressStore.TryGetValue(questId, out var progress) ? progress : null;
        }

        /// <summary>
        /// Record a quest completion. Adds to history and optionally resets
        /// progress for repeatable quests.
        /// </summary>
        public void RecordCompletion(string questId, float xpAwarded, long creditsAwarded)
        {
            if (string.IsNullOrEmpty(questId)) return;

            var record = new CompletionRecord
            {
                QuestId = questId,
                CompletedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                XPAwarded = xpAwarded,
                CreditsAwarded = creditsAwarded
            };

            // Get the quest title for the record
            if (questRecords.TryGetValue(questId, out var questRecord))
            {
                record.QuestTitle = questRecord.QuestData.Title;
            }

            if (!completionHistory.TryGetValue(questId, out var history))
            {
                history = new List<CompletionRecord>();
                completionHistory[questId] = history;
            }

            history.Add(record);

            // Clean up old progress data
            progressStore.Remove(questId);

            // Enforce history size limit
            EnforceHistoryLimit();

            Log($"Quest completed: {record.QuestTitle ?? questId} (+{xpAwarded} XP, +{creditsAwarded} credits)");
            MarkDirty();
        }

        /// <summary>
        /// Clear all progress for a quest (e.g., when abandoned).
        /// </summary>
        public void ClearProgress(string questId)
        {
            if (progressStore.Remove(questId))
                MarkDirty();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Statistics
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Get total XP earned from all completed quests.
        /// </summary>
        public float GetTotalXPFromQuests()
        {
            float total = 0f;
            foreach (var history in completionHistory.Values)
            {
                foreach (var record in history)
                    total += record.XPAwarded;
            }
            return total;
        }

        /// <summary>
        /// Get total credits earned from all completed quests.
        /// </summary>
        public long GetTotalCreditsFromQuests()
        {
            long total = 0;
            foreach (var history in completionHistory.Values)
            {
                foreach (var record in history)
                    total += record.CreditsAwarded;
            }
            return total;
        }

        /// <summary>
        /// Get the number of times a specific quest has been completed.
        /// </summary>
        public int GetCompletionCount(string questId)
        {
            if (completionHistory.TryGetValue(questId, out var history))
                return history.Count;
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Internal Helpers
        // ─────────────────────────────────────────────────────────────────

        private void MarkDirty()
        {
            isDirty = true;
        }

        private void EvictOldestQuest()
        {
            string oldestId = null;
            long oldestTime = long.MaxValue;

            foreach (var kvp in questRecords)
            {
                // Don't evict active quests
                if (kvp.Value.QuestData.State == QuestState.Active)
                    continue;

                if (kvp.Value.ReceivedTimestamp < oldestTime)
                {
                    oldestTime = kvp.Value.ReceivedTimestamp;
                    oldestId = kvp.Key;
                }
            }

            if (oldestId != null)
            {
                questRecords.Remove(oldestId);
                progressStore.Remove(oldestId);
            }
        }

        private void EnforceHistoryLimit()
        {
            int totalRecords = completionHistory.Values.Sum(h => h.Count);
            if (totalRecords <= maxCompletionHistory)
                return;

            // Remove oldest completion records across all quests
            var allRecords = new List<(string questId, CompletionRecord record)>();
            foreach (var kvp in completionHistory)
            {
                foreach (var record in kvp.Value)
                    allRecords.Add((kvp.Key, record));
            }

            allRecords.Sort((a, b) => a.record.CompletedTimestamp.CompareTo(b.record.CompletedTimestamp));

            int toRemove = totalRecords - maxCompletionHistory;
            for (int i = 0; i < toRemove && i < allRecords.Count; i++)
            {
                var (questId, record) = allRecords[i];
                if (completionHistory.TryGetValue(questId, out var history))
                {
                    history.Remove(record);
                    if (history.Count == 0)
                        completionHistory.Remove(questId);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Persistence
        // ─────────────────────────────────────────────────────────────────

        private void PersistToDisk()
        {
            if (persistence == null || !isDirty) return;

            try
            {
                // Save quest definitions
                var questSnapshot = new QuestDatabaseSnapshot
                {
                    Records = new List<QuestRecord>(questRecords.Values),
                    SavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                persistence.Save(QuestsKey, questSnapshot);

                // Save progress data
                var progressSnapshot = new QuestProgressSnapshot
                {
                    Entries = progressStore.Values.ToList()
                };
                persistence.Save(ProgressKey, progressSnapshot);

                // Save completion history
                var historySnapshot = new CompletionHistorySnapshot
                {
                    Entries = new List<CompletionRecord>()
                };
                foreach (var history in completionHistory.Values)
                    historySnapshot.Entries.AddRange(history);
                persistence.Save(HistoryKey, historySnapshot);

                isDirty = false;
            }
            catch (Exception ex)
            {
                Error($"Failed to persist quest database: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            if (persistence == null) return;

            try
            {
                // Load quest definitions
                var questSnapshot = persistence.Load<QuestDatabaseSnapshot>(QuestsKey);
                if (questSnapshot?.Records != null)
                {
                    foreach (var record in questSnapshot.Records)
                    {
                        if (record?.QuestData != null && !string.IsNullOrEmpty(record.QuestData.QuestId))
                            questRecords[record.QuestData.QuestId] = record;
                    }
                }

                // Load progress
                var progressSnapshot = persistence.Load<QuestProgressSnapshot>(ProgressKey);
                if (progressSnapshot?.Entries != null)
                {
                    foreach (var entry in progressSnapshot.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.QuestId))
                            progressStore[entry.QuestId] = entry;
                    }
                }

                // Load completion history
                var historySnapshot = persistence.Load<CompletionHistorySnapshot>(HistoryKey);
                if (historySnapshot?.Entries != null)
                {
                    foreach (var record in historySnapshot.Entries)
                    {
                        if (string.IsNullOrEmpty(record.QuestId)) continue;

                        if (!completionHistory.TryGetValue(record.QuestId, out var history))
                        {
                            history = new List<CompletionRecord>();
                            completionHistory[record.QuestId] = history;
                        }
                        history.Add(record);
                    }
                }

                isDirty = false;
                Log($"Loaded {questRecords.Count} quests, {progressStore.Count} active progress, " +
                    $"{completionHistory.Values.Sum(h => h.Count)} completion records.");
            }
            catch (Exception ex)
            {
                Warn($"Failed to load quest database: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Data Types
        // ─────────────────────────────────────────────────────────────────

        [Serializable]
        private class QuestDatabaseSnapshot
        {
            public List<QuestRecord> Records = new List<QuestRecord>();
            public long SavedTimestamp;
        }

        [Serializable]
        private class QuestProgressSnapshot
        {
            public List<QuestProgressData> Entries = new List<QuestProgressData>();
        }

        [Serializable]
        private class CompletionHistorySnapshot
        {
            public List<CompletionRecord> Entries = new List<CompletionRecord>();
        }
    }

    /// <summary>
    /// Wrapper around QuestData that includes database metadata.
    /// </summary>
    [Serializable]
    public class QuestRecord
    {
        public QuestData QuestData;
        public long ReceivedTimestamp;
        public string Source;  // "system", "network", "faction"
    }

    /// <summary>
    /// Snapshot of progress on an active quest — objective completion state.
    /// </summary>
    [Serializable]
    public class QuestProgressData
    {
        public string QuestId;
        public List<int> ObjectiveProgress = new List<int>();
        public long AcceptedTimestamp;
        public long LastUpdatedTimestamp;
        public float TotalTimeSpentSeconds;
    }

    /// <summary>
    /// Record of a completed quest for history/stats tracking.
    /// </summary>
    [Serializable]
    public class CompletionRecord
    {
        public string QuestId;
        public string QuestTitle;
        public long CompletedTimestamp;
        public float XPAwarded;
        public long CreditsAwarded;
    }
}
