// StatusBarRenderer.cs — Top/bottom status bars showing operative info
// The Daemon's HUD has persistent status displays showing the operative's
// callsign, level, credits, active quests, and network status.

using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using DaemonVision.Core;
using DaemonVision.Identity;
using DaemonVision.Economy;
using DaemonVision.Network;
using DaemonVision.Quest;

namespace DaemonVision.HUD
{
    public class StatusBarRenderer : SubsystemBase
    {
        public override string Name => "StatusBar";

        private DarknetIdentityManager identityManager;
        private DarknetEconomy economy;
        private MeshNetworkManager meshNetwork;
        private QuestManager questManager;

        // Cached status strings (updated periodically, not every frame)
        private string cachedCallsignLine;
        private string cachedStatsLine;
        private string cachedNetworkLine;
        private float updateTimer;
        private const float UpdateInterval = 0.5f;

        public string CallsignLine => cachedCallsignLine;
        public string StatsLine => cachedStatsLine;
        public string NetworkLine => cachedNetworkLine;

        protected override Task OnInitialize()
        {
            cachedCallsignLine = "Initializing...";
            cachedStatsLine = "";
            cachedNetworkLine = "MESH: Connecting...";
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            economy = GetSubsystem<DarknetEconomy>();
            meshNetwork = GetSubsystem<MeshNetworkManager>();
            questManager = GetSubsystem<QuestManager>();

            UpdateDisplay();
        }

        public override void Tick(float deltaTime)
        {
            updateTimer += deltaTime;
            if (updateTimer >= UpdateInterval)
            {
                updateTimer = 0f;
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            var identity = identityManager?.LocalIdentity;

            if (identity != null)
            {
                // Top-left: Callsign and level
                cachedCallsignLine = $"{identity.Callsign}  Lv.{identity.Level}  {identity.DarknetClass}";

                // Top-right: Credits and active quest count
                long credits = economy?.GetBalance() ?? 0;
                int activeQuests = questManager?.ActiveQuestCount ?? 0;
                cachedStatsLine = $"◈ {credits:N0}  |  ⚑ {activeQuests} quests";
            }
            else
            {
                cachedCallsignLine = "UNAUTHENTICATED";
                cachedStatsLine = "---";
            }

            // Bottom: Network status
            int peers = meshNetwork?.ConnectedPeerCount ?? 0;
            bool meshActive = meshNetwork?.IsActive ?? false;

            if (meshActive)
            {
                cachedNetworkLine = $"MESH: {peers} peers  |  D-SPACE ONLINE";
            }
            else
            {
                cachedNetworkLine = "MESH: Offline  |  D-SPACE LIMITED";
            }
        }
    }
}
