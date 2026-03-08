// QuestHUDRenderer.cs — Quest thread visualization in D-Space
// In the Daemon, quest objectives appear as glowing paths through the real world,
// guiding operatives along "quest threads" — visible AR trails that lead to objectives.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Quest;

namespace DaemonVision.HUD
{
    public class QuestHUDRenderer : SubsystemBase
    {
        public override string Name => "QuestHUD";

        [Header("Quest Thread Visualization")]
        [SerializeField] private Material questPathMaterial;
        [SerializeField] private float pathWidth = 0.1f;
        [SerializeField] private float pathHeightOffset = 0.05f; // Slightly above ground
        [SerializeField] private float pathAnimSpeed = 2f;
        [SerializeField] private int pathSegments = 50;
        [SerializeField] private float maxPathLength = 200f; // meters

        [Header("Waypoint Markers")]
        [SerializeField] private GameObject waypointMarkerPrefab;
        [SerializeField] private float waypointPulseSpeed = 1.5f;
        [SerializeField] private float waypointScale = 0.5f;

        private QuestManager questManager;
        private HUDManager hudManager;

        private readonly Dictionary<string, QuestPathVisual> activePaths
            = new Dictionary<string, QuestPathVisual>();

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            questManager = GetSubsystem<QuestManager>();
            hudManager = GetSubsystem<HUDManager>();

            if (questManager != null)
            {
                questManager.OnQuestAccepted += HandleQuestAccepted;
                questManager.OnQuestCompleted += HandleQuestCompleted;
                questManager.OnQuestAbandoned += HandleQuestAbandoned;
                questManager.OnObjectiveUpdated += HandleObjectiveUpdated;
            }
        }

        public override void Tick(float deltaTime)
        {
            foreach (var kvp in activePaths)
            {
                var visual = kvp.Value;

                // Animate the quest path (flowing particles/glow)
                if (visual.LineRenderer != null)
                {
                    visual.AnimOffset += deltaTime * pathAnimSpeed;
                    if (questPathMaterial != null)
                    {
                        questPathMaterial.SetFloat("_AnimOffset", visual.AnimOffset);
                    }
                }

                // Pulse waypoint markers
                if (visual.WaypointMarker != null)
                {
                    float pulse = 1f + Mathf.Sin(Time.time * waypointPulseSpeed) * 0.15f;
                    visual.WaypointMarker.transform.localScale = Vector3.one * waypointScale * pulse;

                    // Rotate slowly
                    visual.WaypointMarker.transform.Rotate(Vector3.up, 30f * deltaTime);
                }
            }
        }

        private void HandleQuestAccepted(QuestData quest)
        {
            CreateQuestPath(quest);
        }

        private void HandleObjectiveUpdated(string questId, int objectiveIndex)
        {
            if (activePaths.TryGetValue(questId, out var visual))
            {
                // Rebuild path to new objective
                RebuildQuestPath(visual, questId);
            }
        }

        private void HandleQuestCompleted(string questId)
        {
            RemoveQuestPath(questId);
        }

        private void HandleQuestAbandoned(string questId)
        {
            RemoveQuestPath(questId);
        }

        private void CreateQuestPath(QuestData quest)
        {
            if (activePaths.ContainsKey(quest.QuestId))
                return;

            var pathGO = new GameObject($"QuestPath_{quest.QuestId}");
            pathGO.transform.SetParent(Manager.WorldAnchorRoot);

            var lineRenderer = pathGO.AddComponent<LineRenderer>();
            lineRenderer.material = questPathMaterial;
            lineRenderer.startWidth = pathWidth;
            lineRenderer.endWidth = pathWidth;
            lineRenderer.positionCount = 0;
            lineRenderer.useWorldSpace = true;

            // Set quest thread color (gold by default, as in the Daemon)
            Color questColor = hudManager?.Colors?.QuestThread ?? new Color(1f, 0.85f, 0f, 0.8f);
            lineRenderer.startColor = questColor;
            lineRenderer.endColor = questColor;

            // Create waypoint marker at current objective
            GameObject waypointMarker = null;
            var currentObjective = quest.GetCurrentObjective();
            if (currentObjective != null && currentObjective.TargetPosition.HasValue)
            {
                waypointMarker = CreateWaypointMarker(currentObjective.TargetPosition.Value, questColor);
            }

            var visual = new QuestPathVisual
            {
                QuestId = quest.QuestId,
                PathObject = pathGO,
                LineRenderer = lineRenderer,
                WaypointMarker = waypointMarker,
                AnimOffset = 0f
            };

            activePaths[quest.QuestId] = visual;
            BuildPathPoints(visual, quest);

            Log($"Quest path created: {quest.Title}");
        }

        private void BuildPathPoints(QuestPathVisual visual, QuestData quest)
        {
            var objective = quest.GetCurrentObjective();
            if (objective?.TargetPosition == null) return;

            var camera = Manager.ARCamera;
            if (camera == null) return;

            Vector3 start = camera.transform.position;
            Vector3 end = objective.TargetPosition.Value;
            Vector3 direction = end - start;
            float distance = Mathf.Min(direction.magnitude, maxPathLength);

            // Generate path points with slight curve
            var points = new Vector3[pathSegments];
            for (int i = 0; i < pathSegments; i++)
            {
                float t = (float)i / (pathSegments - 1);
                Vector3 point = Vector3.Lerp(start, start + direction.normalized * distance, t);
                point.y = pathHeightOffset; // Keep near ground level
                points[i] = point;
            }

            visual.LineRenderer.positionCount = pathSegments;
            visual.LineRenderer.SetPositions(points);
        }

        private void RebuildQuestPath(QuestPathVisual visual, string questId)
        {
            var quest = questManager?.GetQuest(questId);
            if (quest == null) return;

            var objective = quest.GetCurrentObjective();
            if (objective?.TargetPosition != null && visual.WaypointMarker != null)
            {
                visual.WaypointMarker.transform.position = objective.TargetPosition.Value;
            }

            BuildPathPoints(visual, quest);
        }

        private GameObject CreateWaypointMarker(Vector3 position, Color color)
        {
            // Create a glowing diamond-shaped waypoint marker
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "WaypointMarker";
            marker.transform.SetParent(Manager.WorldAnchorRoot);
            marker.transform.position = position + Vector3.up * 2f;
            marker.transform.rotation = Quaternion.Euler(45, 0, 45);
            marker.transform.localScale = Vector3.one * waypointScale;

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = color;
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", color * 2f);
            }

            // Remove collider (visual only)
            var collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            return marker;
        }

        private void RemoveQuestPath(string questId)
        {
            if (activePaths.TryGetValue(questId, out var visual))
            {
                activePaths.Remove(questId);
                if (visual.PathObject != null) Destroy(visual.PathObject);
                if (visual.WaypointMarker != null) Destroy(visual.WaypointMarker);
            }
        }

        protected override void OnShutdown()
        {
            foreach (var kvp in activePaths)
            {
                if (kvp.Value.PathObject != null) Destroy(kvp.Value.PathObject);
                if (kvp.Value.WaypointMarker != null) Destroy(kvp.Value.WaypointMarker);
            }
            activePaths.Clear();
        }
    }

    public class QuestPathVisual
    {
        public string QuestId;
        public GameObject PathObject;
        public LineRenderer LineRenderer;
        public GameObject WaypointMarker;
        public float AnimOffset;
    }
}
