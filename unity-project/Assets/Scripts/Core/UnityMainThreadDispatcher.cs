// UnityMainThreadDispatcher.cs - Marshals work from network threads onto Unity's main thread
// UDP receive loops run on thread-pool threads. Unity APIs (Time, GameObject,
// events that touch scene objects) are main-thread only, so those loops hand
// their results here and the main thread drains the queue once per frame.

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DaemonVision.Core
{
    public sealed class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> pending = new Queue<Action>();
        private static readonly List<Action> drainBuffer = new List<Action>();
        private static UnityMainThreadDispatcher instance;
        private static int mainThreadId = -1;

        /// <summary>
        /// True when called from the thread that runs Unity's game loop.
        /// Unknown (false) until <see cref="EnsureExists"/> has run once.
        /// </summary>
        public static bool IsMainThread =>
            mainThreadId != -1 && Thread.CurrentThread.ManagedThreadId == mainThreadId;

        /// <summary>
        /// Create the dispatcher object if the scene does not already have one.
        /// Must be called from the main thread, typically during subsystem init.
        /// </summary>
        public static void EnsureExists()
        {
            if (instance != null) return;

            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var go = new GameObject("[D-Space MainThreadDispatcher]");
            go.hideFlags = HideFlags.HideAndDontSave;
            instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Queue an action to run on the next main-thread Update. Safe from any thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (pending)
            {
                pending.Enqueue(action);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Update()
        {
            lock (pending)
            {
                if (pending.Count == 0) return;
                drainBuffer.Clear();
                while (pending.Count > 0)
                    drainBuffer.Add(pending.Dequeue());
            }

            // Run outside the lock so a callback that enqueues more work cannot deadlock.
            foreach (var action in drainBuffer)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MainThreadDispatcher] Queued action threw: {ex}");
                }
            }
            drainBuffer.Clear();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
