// VoiceCommandProcessor.cs — Voice command interface for D-Space
// In the Daemon, operatives can issue voice commands to interact with D-Space.
// "Open map", "Accept quest", "Scan area", "Send message", etc.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Input
{
    public class VoiceCommandProcessor : SubsystemBase
    {
        public override string Name => "VoiceCommands";

        [Header("Voice Command Settings")]
        [SerializeField] private string wakeWord = "daemon";
        [SerializeField] private float listeningTimeout = 5f;
        [SerializeField] private bool requireWakeWord = true;
        [SerializeField] private bool continuousListening;

        private bool isListening;
        private float listenTimer;

        private readonly Dictionary<string, Action<string[]>> commands
            = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);

        public bool IsListening => isListening;

        public event Action<string> OnCommandRecognized;
        public event Action<string> OnCommandFailed;

        protected override Task OnInitialize()
        {
            RegisterDefaultCommands();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Register a voice command handler.
        /// </summary>
        public void RegisterCommand(string phrase, Action<string[]> handler)
        {
            commands[phrase.ToLower()] = handler;
        }

        /// <summary>
        /// Process recognized speech text and match to commands.
        /// Called by the platform's speech recognition system.
        /// </summary>
        public void ProcessSpeech(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;

            string text = recognizedText.Trim().ToLower();
            Log($"Speech: \"{text}\"");

            // Check for wake word if required
            if (requireWakeWord && !isListening)
            {
                if (text.Contains(wakeWord.ToLower()))
                {
                    isListening = true;
                    listenTimer = 0f;
                    Log("Wake word detected. Listening...");
                    text = text.Substring(text.IndexOf(wakeWord, StringComparison.OrdinalIgnoreCase)
                        + wakeWord.Length).Trim();

                    if (string.IsNullOrEmpty(text)) return;
                }
                else return;
            }

            // Match command
            bool matched = false;
            foreach (var kvp in commands)
            {
                if (text.StartsWith(kvp.Key))
                {
                    string[] args = text.Substring(kvp.Key.Length).Trim().Split(' ',
                        StringSplitOptions.RemoveEmptyEntries);
                    kvp.Value.Invoke(args);
                    OnCommandRecognized?.Invoke(kvp.Key);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                OnCommandFailed?.Invoke(text);
                Warn($"Unrecognized command: \"{text}\"");
            }

            if (!continuousListening)
                isListening = false;
        }

        public override void Tick(float deltaTime)
        {
            if (isListening && !continuousListening)
            {
                listenTimer += deltaTime;
                if (listenTimer > listeningTimeout)
                {
                    isListening = false;
                    Log("Listening timeout.");
                }
            }
        }

        private void RegisterDefaultCommands()
        {
            RegisterCommand("scan", args =>
                Log("Scanning area..."));

            RegisterCommand("accept quest", args =>
                Log("Accepting current quest..."));

            RegisterCommand("abandon quest", args =>
                Log("Abandoning current quest..."));

            RegisterCommand("open map", args =>
                Log("Opening map overlay..."));

            RegisterCommand("close map", args =>
                Log("Closing map overlay..."));

            RegisterCommand("show quests", args =>
                Log("Showing quest log..."));

            RegisterCommand("send message", args =>
            {
                string message = string.Join(" ", args);
                Log($"Sending: {message}");
            });

            RegisterCommand("status", args =>
                Log("Displaying status..."));

            RegisterCommand("identify", args =>
                Log("Identifying target..."));

            RegisterCommand("mark threat", args =>
                Log("Marking target as threat..."));

            RegisterCommand("navigate to", args =>
                Log($"Navigating to: {string.Join(" ", args)}"));

            RegisterCommand("help", args =>
                Log("Available commands: scan, accept quest, open map, show quests, send message, status, identify, mark threat, navigate to"));
        }
    }
}
