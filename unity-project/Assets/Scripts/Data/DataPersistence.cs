// DataPersistence.cs — JSON-based data persistence layer for D-Space
// Handles save/load of all persistent state: identities, anchors, quests,
// economy, reputation. Thread-safe write queue with background flushing,
// auto-save interval, backup creation, and corruption recovery.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Data
{
    /// <summary>
    /// Central persistence layer for all D-Space data. Serializes objects to JSON
    /// files in Application.persistentDataPath. Thread-safe — writes are queued and
    /// flushed on a background thread. Creates .bak files before each write for
    /// corruption recovery.
    /// </summary>
    public class DataPersistence : SubsystemBase
    {
        public override string Name => "DataPersistence";

        [Header("Persistence Settings")]
        [SerializeField] private float autoSaveIntervalSeconds = 60f;
        [SerializeField] private int maxWriteQueueSize = 100;
        [SerializeField] private string saveSubdirectory = "DSpaceData";

        /// <summary>
        /// Root directory for all save files.
        /// </summary>
        public string SaveRoot { get; private set; }

        // Thread-safe write queue: key -> (json, path)
        private readonly ConcurrentDictionary<string, PendingWrite> writeQueue
            = new ConcurrentDictionary<string, PendingWrite>();

        // In-memory cache of recently loaded data to avoid repeated disk reads
        private readonly ConcurrentDictionary<string, string> cache
            = new ConcurrentDictionary<string, string>();

        private float autoSaveTimer;
        private CancellationTokenSource cancellationSource;
        private volatile bool isShuttingDown;
        private readonly object flushLock = new object();
        private volatile bool isFlushing;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected override Task OnInitialize()
        {
            SaveRoot = Path.Combine(Application.persistentDataPath, saveSubdirectory);

            try
            {
                if (!Directory.Exists(SaveRoot))
                {
                    Directory.CreateDirectory(SaveRoot);
                    Log($"Created save directory: {SaveRoot}");
                }
            }
            catch (Exception ex)
            {
                Error($"Failed to create save directory: {ex.Message}");
                return Task.CompletedTask;
            }

            cancellationSource = new CancellationTokenSource();

            Log($"Persistence online. Save path: {SaveRoot}");
            return Task.CompletedTask;
        }

        public override void Tick(float deltaTime)
        {
            autoSaveTimer += deltaTime;

            if (autoSaveTimer >= autoSaveIntervalSeconds)
            {
                autoSaveTimer = 0f;
                SaveAll();
            }
        }

        protected override void OnShutdown()
        {
            isShuttingDown = true;

            // Flush all pending writes synchronously on shutdown
            FlushWriteQueueSync();

            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            cancellationSource = null;

            Log("Persistence shut down. All data flushed.");
        }

        /// <summary>
        /// Called by Unity when the application is quitting — ensures data is saved.
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && IsActive)
            {
                SaveAll();
            }
        }

        private void OnApplicationQuit()
        {
            if (IsActive)
            {
                FlushWriteQueueSync();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Save data to persistent storage. Serializes to JSON and queues for
        /// background write. The write is deduped by key — only the latest
        /// value is written if multiple saves occur before flush.
        /// </summary>
        public void Save<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
            {
                Warn("Save called with null/empty key.");
                return;
            }

            try
            {
                string json = DSpaceSerializer.Serialize(data);
                string path = GetSavePath(key);

                // Update cache
                cache[key] = json;

                // Queue write (deduplicated by key)
                var pending = new PendingWrite
                {
                    Key = key,
                    Json = json,
                    FilePath = path,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                writeQueue[key] = pending;

                if (writeQueue.Count > maxWriteQueueSize)
                {
                    Warn($"Write queue exceeds max size ({maxWriteQueueSize}). Forcing flush.");
                    FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Error($"Failed to serialize data for key '{key}': {ex.Message}");
            }
        }

        /// <summary>
        /// Load data from persistent storage. Checks the in-memory cache first,
        /// then reads from disk. If the primary file is corrupted, attempts to
        /// load from the .bak backup.
        /// </summary>
        public T Load<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Warn("Load called with null/empty key.");
                return default;
            }

            // Check cache first
            if (cache.TryGetValue(key, out string cached))
            {
                try
                {
                    return DSpaceSerializer.Deserialize<T>(cached);
                }
                catch
                {
                    // Cache was invalid; fall through to disk
                    cache.TryRemove(key, out _);
                }
            }

            string path = GetSavePath(key);

            // Try primary file
            string json = ReadFileWithRecovery(path);
            if (json != null)
            {
                try
                {
                    T result = DSpaceSerializer.Deserialize<T>(json);
                    cache[key] = json;
                    return result;
                }
                catch (Exception ex)
                {
                    Warn($"Primary file corrupted for key '{key}': {ex.Message}. Trying backup...");
                }
            }

            // Try backup file
            string backupPath = path + ".bak";
            string backupJson = ReadFileSafe(backupPath);
            if (backupJson != null)
            {
                try
                {
                    T result = DSpaceSerializer.Deserialize<T>(backupJson);
                    cache[key] = backupJson;

                    // Restore backup as primary
                    WriteFileSafe(path, backupJson);
                    Log($"Recovered data for key '{key}' from backup.");

                    return result;
                }
                catch (Exception ex)
                {
                    Error($"Backup also corrupted for key '{key}': {ex.Message}");
                }
            }

            return default;
        }

        /// <summary>
        /// Delete a persisted key and its backup.
        /// </summary>
        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            string path = GetSavePath(key);

            // Remove from queue and cache
            writeQueue.TryRemove(key, out _);
            cache.TryRemove(key, out _);

            // Delete files
            DeleteFileSafe(path);
            DeleteFileSafe(path + ".bak");

            Log($"Deleted key: {key}");
        }

        /// <summary>
        /// Check if a key exists in the cache or on disk.
        /// </summary>
        public bool Exists(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (cache.ContainsKey(key))
                return true;

            if (writeQueue.ContainsKey(key))
                return true;

            string path = GetSavePath(key);
            return File.Exists(path);
        }

        /// <summary>
        /// Flush all pending writes. Fires and forgets a background task unless
        /// shutting down, in which case it flushes synchronously.
        /// </summary>
        public void SaveAll()
        {
            if (isShuttingDown)
            {
                FlushWriteQueueSync();
            }
            else
            {
                FlushAsync();
            }
        }

        /// <summary>
        /// Get the full filesystem path for a persistence key.
        /// Keys can contain '/' to create subdirectories.
        /// </summary>
        public string GetSavePath(string key)
        {
            // Sanitize the key — replace invalid path chars but allow '/' for subdirs
            string sanitized = key
                .Replace('\\', '/')
                .Replace(':', '_')
                .Replace('*', '_')
                .Replace('?', '_')
                .Replace('"', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('|', '_');

            if (!sanitized.EndsWith(".json"))
                sanitized += ".json";

            return Path.Combine(SaveRoot, sanitized);
        }

        /// <summary>
        /// Get all keys currently stored on disk.
        /// </summary>
        public List<string> GetAllKeys()
        {
            var keys = new List<string>();
            if (!Directory.Exists(SaveRoot)) return keys;

            try
            {
                string[] files = Directory.GetFiles(SaveRoot, "*.json", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    if (file.EndsWith(".bak.json")) continue;

                    string relative = file
                        .Substring(SaveRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');

                    // Remove .json extension to get the key
                    if (relative.EndsWith(".json"))
                        relative = relative.Substring(0, relative.Length - 5);

                    keys.Add(relative);
                }
            }
            catch (Exception ex)
            {
                Error($"Failed to enumerate keys: {ex.Message}");
            }

            return keys;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Background Write Queue
        // ─────────────────────────────────────────────────────────────────

        private void FlushAsync()
        {
            if (isFlushing || writeQueue.IsEmpty)
                return;

            Task.Run(() => FlushWriteQueueSync());
        }

        private void FlushWriteQueueSync()
        {
            if (writeQueue.IsEmpty)
                return;

            lock (flushLock)
            {
                if (writeQueue.IsEmpty) return;
                isFlushing = true;

                try
                {
                    // Snapshot and clear the queue
                    var snapshot = new List<PendingWrite>();
                    foreach (var key in writeQueue.Keys)
                    {
                        if (writeQueue.TryRemove(key, out var pending))
                            snapshot.Add(pending);
                    }

                    foreach (var pending in snapshot)
                    {
                        try
                        {
                            WriteToDisk(pending.FilePath, pending.Json);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DataPersistence] Write failed for '{pending.Key}': {ex.Message}");
                        }
                    }
                }
                finally
                {
                    isFlushing = false;
                }
            }
        }

        /// <summary>
        /// Write JSON to disk with backup creation. Creates the .bak first by
        /// copying the existing file, then writes the new data atomically.
        /// </summary>
        private void WriteToDisk(string filePath, string json)
        {
            // Ensure directory exists
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string backupPath = filePath + ".bak";

            // Create backup of existing file
            if (File.Exists(filePath))
            {
                try
                {
                    File.Copy(filePath, backupPath, true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DataPersistence] Backup creation failed: {ex.Message}");
                }
            }

            // Write to temp file first, then move — atomic on most filesystems
            string tempPath = filePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);

                // Replace original with temp
                if (File.Exists(filePath))
                    File.Delete(filePath);

                File.Move(tempPath, filePath);
            }
            catch
            {
                // If atomic write failed, try direct write
                DeleteFileSafe(tempPath);
                File.WriteAllText(filePath, json);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  File I/O Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Read a file with corruption detection. Returns null if file doesn't
        /// exist or appears corrupted (empty, incomplete JSON).
        /// </summary>
        private string ReadFileWithRecovery(string path)
        {
            string json = ReadFileSafe(path);
            if (json == null) return null;

            // Basic corruption check: valid JSON should start with { or [
            string trimmed = json.Trim();
            if (trimmed.Length == 0)
                return null;

            char first = trimmed[0];
            if (first != '{' && first != '[' && first != '"' &&
                first != 't' && first != 'f' && first != 'n' &&
                !char.IsDigit(first) && first != '-')
            {
                Warn($"File appears corrupted (unexpected first char '{first}'): {path}");
                return null;
            }

            return json;
        }

        private static string ReadFileSafe(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteFileSafe(string path, string content)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, content);
            }
            catch
            {
                // Silently fail — best effort
            }
        }

        private static void DeleteFileSafe(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Silently fail
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Internal Types
        // ─────────────────────────────────────────────────────────────────

        private struct PendingWrite
        {
            public string Key;
            public string Json;
            public string FilePath;
            public long Timestamp;
        }
    }
}
