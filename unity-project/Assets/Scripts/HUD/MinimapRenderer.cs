// MinimapRenderer.cs — D-Space tactical minimap overlay
// Shows nearby operatives, quest objectives, threats, and D-Space anchors
// on a radar-style display in the corner of the HUD.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;
using DaemonVision.Spatial;

namespace DaemonVision.HUD
{
    public class MinimapRenderer : SubsystemBase
    {
        public override string Name => "Minimap";

        [Header("Minimap Settings")]
        [SerializeField] private float minimapRadius = 200f;    // meters
        [SerializeField] private float minimapSize = 150f;       // pixels
        [SerializeField] private bool rotateWithPlayer = true;
        [SerializeField] private float blipSize = 6f;

        private DarknetIdentityManager identityManager;
        private SpatialAnchorManager anchorManager;
        private Camera arCamera;

        private readonly List<MinimapBlip> blips = new List<MinimapBlip>();

        protected override Task OnInitialize()
        {
            arCamera = Manager.ARCamera;
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            anchorManager = GetSubsystem<SpatialAnchorManager>();
        }

        public override void Tick(float deltaTime)
        {
            if (arCamera == null) return;

            blips.Clear();
            Vector3 playerPos = arCamera.transform.position;
            float heading = rotateWithPlayer ? arCamera.transform.eulerAngles.y : 0f;

            // Add operative blips
            if (identityManager != null)
            {
                foreach (var identity in identityManager.GetAllIdentities())
                {
                    if (identity.DarknetAddress == identityManager.LocalIdentity?.DarknetAddress)
                        continue;

                    Vector3 relPos = identity.LastKnownPosition - playerPos;
                    if (relPos.magnitude > minimapRadius) continue;

                    Vector2 minimapPos = WorldToMinimap(relPos, heading);

                    blips.Add(new MinimapBlip
                    {
                        Position = minimapPos,
                        Color = GetBlipColor(identity),
                        Size = blipSize,
                        Label = identity.Callsign,
                        Type = MinimapBlipType.Operative
                    });
                }
            }

            // Add anchor blips
            if (anchorManager != null)
            {
                foreach (var anchor in anchorManager.GetAnchorsInRadius(playerPos, minimapRadius))
                {
                    Vector3 relPos = anchor.transform.position - playerPos;
                    Vector2 minimapPos = WorldToMinimap(relPos, heading);

                    blips.Add(new MinimapBlip
                    {
                        Position = minimapPos,
                        Color = GetAnchorColor(anchor.Data.AnchorType),
                        Size = blipSize * 0.8f,
                        Label = anchor.Data.AnchorType.ToString(),
                        Type = MinimapBlipType.Anchor
                    });
                }
            }
        }

        public IReadOnlyList<MinimapBlip> GetBlips() => blips;

        public float GetMinimapRadius() => minimapRadius;
        public float GetMinimapSize() => minimapSize;

        private Vector2 WorldToMinimap(Vector3 worldOffset, float heading)
        {
            // Rotate offset by negative heading so minimap rotates with player
            float rad = -heading * Mathf.Deg2Rad;
            float x = worldOffset.x * Mathf.Cos(rad) - worldOffset.z * Mathf.Sin(rad);
            float y = worldOffset.x * Mathf.Sin(rad) + worldOffset.z * Mathf.Cos(rad);

            // Scale to minimap size
            float scale = (minimapSize * 0.5f) / minimapRadius;
            return new Vector2(x * scale, y * scale);
        }

        private Color GetBlipColor(DarknetIdentity identity)
        {
            return identity.LocalThreatLevel switch
            {
                ThreatLevel.High or ThreatLevel.Critical => Color.red,
                ThreatLevel.Moderate => new Color(1f, 0.5f, 0f),
                _ => new Color(0f, 0.8f, 1f)
            };
        }

        private Color GetAnchorColor(DSpaceAnchorType type)
        {
            return type switch
            {
                DSpaceAnchorType.QuestGiver => new Color(1f, 0.85f, 0f),
                DSpaceAnchorType.Cache => new Color(0f, 1f, 0.5f),
                DSpaceAnchorType.Hazard => Color.red,
                DSpaceAnchorType.MeetingPoint => new Color(0.5f, 0.5f, 1f),
                _ => new Color(0.6f, 0.6f, 0.6f)
            };
        }
    }

    public struct MinimapBlip
    {
        public Vector2 Position;
        public Color Color;
        public float Size;
        public string Label;
        public MinimapBlipType Type;
    }

    public enum MinimapBlipType
    {
        Operative,
        Anchor,
        QuestObjective,
        Threat,
        Self
    }
}
