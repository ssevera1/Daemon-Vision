// NameplateRenderer.cs — Floating call-outs above people in D-Space
// THE signature visual of the Daemon: every operative has a floating nameplate
// showing their callsign, level, class, faction, and reputation stars.
// Hostiles get red outlines. Unknown people show as gray "Unnamed" tags.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;
using DaemonVision.Identity;
using DaemonVision.Detection;

namespace DaemonVision.HUD
{
    public class NameplateRenderer : SubsystemBase
    {
        public override string Name => "Nameplates";

        [Header("Nameplate Settings")]
        [SerializeField] private GameObject nameplatePrefab;
        [SerializeField] private float nameplateHeight = 0.3f;    // Meters above head
        [SerializeField] private float maxRenderDistance = 50f;
        [SerializeField] private float minRenderDistance = 1f;
        [SerializeField] private float fadeStartDistance = 40f;
        [SerializeField] private float nameplateScale = 0.005f;
        [SerializeField] private bool faceCamera = true;

        private DarknetIdentityManager identityManager;
        private PersonDetector personDetector;
        private HUDManager hudManager;

        private readonly Dictionary<string, NameplateInstance> activeNameplates
            = new Dictionary<string, NameplateInstance>();
        private readonly Queue<NameplateInstance> nameplatePool = new Queue<NameplateInstance>();
        private const int PoolSize = 20;

        protected override Task OnInitialize()
        {
            // Pre-populate nameplate pool
            for (int i = 0; i < PoolSize; i++)
            {
                var instance = CreateNameplateInstance();
                instance.Root.SetActive(false);
                nameplatePool.Enqueue(instance);
            }

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            identityManager = GetSubsystem<DarknetIdentityManager>();
            personDetector = GetSubsystem<PersonDetector>();
            hudManager = GetSubsystem<HUDManager>();

            if (personDetector != null)
            {
                personDetector.OnPersonDetected += HandlePersonDetected;
                personDetector.OnPersonLost += HandlePersonLost;
                personDetector.OnPersonUpdated += HandlePersonUpdated;
            }
        }

        public override void Tick(float deltaTime)
        {
            var camera = Manager.ARCamera;
            if (camera == null) return;

            var toRemove = new List<string>();

            foreach (var kvp in activeNameplates)
            {
                var np = kvp.Value;
                if (np.Root == null || !np.Root.activeSelf)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Position: float above the tracked person's head
                Vector3 targetPos = np.TrackedPosition + Vector3.up * nameplateHeight;
                np.Root.transform.position = Vector3.Lerp(
                    np.Root.transform.position, targetPos, deltaTime * 10f);

                // Billboard: always face the camera
                if (faceCamera)
                {
                    np.Root.transform.LookAt(
                        np.Root.transform.position + camera.transform.forward);
                }

                // Distance-based scaling and fading
                float distance = Vector3.Distance(camera.transform.position, np.Root.transform.position);
                if (distance > maxRenderDistance || distance < minRenderDistance)
                {
                    np.Root.SetActive(false);
                    continue;
                }

                float scaleFactor = Mathf.Clamp(distance * nameplateScale, 0.5f, 3f);
                np.Root.transform.localScale = Vector3.one * scaleFactor;

                float alpha = distance > fadeStartDistance
                    ? 1f - ((distance - fadeStartDistance) / (maxRenderDistance - fadeStartDistance))
                    : 1f;
                if (np.CanvasGroup != null)
                    np.CanvasGroup.alpha = alpha * (hudManager?.HUDOpacity ?? 1f);
            }

            foreach (var id in toRemove)
            {
                ReturnNameplate(id);
            }
        }

        private void HandlePersonDetected(DetectedPerson person)
        {
            if (activeNameplates.ContainsKey(person.TrackingId))
                return;

            var np = GetOrCreateNameplate();
            np.TrackedPosition = person.WorldPosition;
            np.Root.SetActive(true);
            np.Root.transform.position = person.WorldPosition + Vector3.up * nameplateHeight;

            // Try to match with a known darknet identity
            var identity = TryMatchIdentity(person);
            UpdateNameplateContent(np, identity, person);

            activeNameplates[person.TrackingId] = np;
        }

        private void HandlePersonUpdated(DetectedPerson person)
        {
            if (activeNameplates.TryGetValue(person.TrackingId, out var np))
            {
                np.TrackedPosition = person.WorldPosition;
            }
        }

        private void HandlePersonLost(string trackingId)
        {
            ReturnNameplate(trackingId);
        }

        private void UpdateNameplateContent(NameplateInstance np, DarknetIdentity identity, DetectedPerson person)
        {
            var colors = hudManager?.Colors ?? new HUDColorScheme();

            if (identity != null)
            {
                // Known darknet operative
                np.CallsignText.text = identity.Callsign;
                np.LevelText.text = $"Lv.{identity.Level}";
                np.ClassText.text = identity.DarknetClass.ToString();
                np.FactionText.text = identity.Faction;

                // Reputation stars — the Daemon's 5-star system
                np.ReputationText.text = GetStarString(identity.ReputationStars)
                    + $" ({identity.ReputationCount})";

                // Color based on threat level
                Color nameplateColor = identity.LocalThreatLevel switch
                {
                    ThreatLevel.None => colors.NameplateFriendly,
                    ThreatLevel.Low => colors.NameplateFriendly,
                    ThreatLevel.Moderate => colors.Warning,
                    ThreatLevel.High => colors.NameplateHostile,
                    ThreatLevel.Critical => colors.Danger,
                    _ => colors.NameplateNeutral
                };

                np.BackgroundImage.color = new Color(
                    nameplateColor.r * 0.2f,
                    nameplateColor.g * 0.2f,
                    nameplateColor.b * 0.2f,
                    0.7f);
                np.BorderImage.color = nameplateColor;
                np.CallsignText.color = nameplateColor;
            }
            else
            {
                // Unknown person — gray "Unnamed" tag
                np.CallsignText.text = "Unnamed";
                np.LevelText.text = "";
                np.ClassText.text = "";
                np.FactionText.text = "";
                np.ReputationText.text = "";
                np.BackgroundImage.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
                np.BorderImage.color = colors.Neutral;
                np.CallsignText.color = colors.Neutral;
            }
        }

        private DarknetIdentity TryMatchIdentity(DetectedPerson person)
        {
            if (identityManager == null) return null;

            // In production, match via mesh network broadcast + face recognition
            // For now, check if any known identity is near this person's position
            foreach (var identity in identityManager.GetAllIdentities())
            {
                if (identity.DarknetAddress == identityManager.LocalIdentity?.DarknetAddress)
                    continue;

                float dist = Vector3.Distance(identity.LastKnownPosition, person.WorldPosition);
                if (dist < 2f)
                    return identity;
            }

            return null;
        }

        private string GetStarString(float stars)
        {
            int fullStars = Mathf.FloorToInt(stars);
            bool halfStar = (stars - fullStars) >= 0.5f;
            int emptyStars = 5 - fullStars - (halfStar ? 1 : 0);

            return new string('★', fullStars)
                 + (halfStar ? "☆" : "")
                 + new string('☆', emptyStars);
        }

        private NameplateInstance GetOrCreateNameplate()
        {
            if (nameplatePool.Count > 0)
                return nameplatePool.Dequeue();
            return CreateNameplateInstance();
        }

        private void ReturnNameplate(string trackingId)
        {
            if (activeNameplates.TryGetValue(trackingId, out var np))
            {
                activeNameplates.Remove(trackingId);
                np.Root.SetActive(false);
                nameplatePool.Enqueue(np);
            }
        }

        private NameplateInstance CreateNameplateInstance()
        {
            // Build nameplate UI programmatically (or use prefab in production)
            var root = new GameObject("Nameplate");
            root.transform.SetParent(Manager.WorldAnchorRoot);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 120);

            var canvasGroup = root.AddComponent<CanvasGroup>();

            // Background panel
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(root.transform, false);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0.05f, 0.05f, 0.1f, 0.7f);
            var bgRT = bgImage.rectTransform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Border
            var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(root.transform, false);
            var borderImage = borderGO.AddComponent<Image>();
            borderImage.color = new Color(0, 0.75f, 1f, 0.9f);
            borderImage.type = Image.Type.Sliced;
            var borderRT = borderImage.rectTransform;
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = new Vector2(-2, -2);
            borderRT.offsetMax = new Vector2(2, 2);

            // Text elements
            var callsign = CreateText(root.transform, "Callsign", 24, FontStyles.Bold,
                new Vector2(0, 0.6f), new Vector2(1, 1));
            var level = CreateText(root.transform, "Level", 16, FontStyles.Normal,
                new Vector2(0, 0.35f), new Vector2(0.35f, 0.6f));
            var classText = CreateText(root.transform, "Class", 16, FontStyles.Normal,
                new Vector2(0.35f, 0.35f), new Vector2(1, 0.6f));
            var faction = CreateText(root.transform, "Faction", 14, FontStyles.Italic,
                new Vector2(0, 0.1f), new Vector2(1, 0.35f));
            var reputation = CreateText(root.transform, "Reputation", 14, FontStyles.Normal,
                new Vector2(0, -0.1f), new Vector2(1, 0.1f));

            return new NameplateInstance
            {
                Root = root,
                CanvasGroup = canvasGroup,
                BackgroundImage = bgImage,
                BorderImage = borderImage,
                CallsignText = callsign,
                LevelText = level,
                ClassText = classText,
                FactionText = faction,
                ReputationText = reputation
            };
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float fontSize,
            FontStyles style, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var rt = tmp.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(8, 0);
            rt.offsetMax = new Vector2(-8, 0);

            return tmp;
        }

        protected override void OnShutdown()
        {
            foreach (var kvp in activeNameplates)
            {
                if (kvp.Value.Root != null)
                    Destroy(kvp.Value.Root);
            }
            activeNameplates.Clear();

            while (nameplatePool.Count > 0)
            {
                var np = nameplatePool.Dequeue();
                if (np.Root != null)
                    Destroy(np.Root);
            }
        }
    }

    public class NameplateInstance
    {
        public GameObject Root;
        public CanvasGroup CanvasGroup;
        public Image BackgroundImage;
        public Image BorderImage;
        public TextMeshProUGUI CallsignText;
        public TextMeshProUGUI LevelText;
        public TextMeshProUGUI ClassText;
        public TextMeshProUGUI FactionText;
        public TextMeshProUGUI ReputationText;
        public Vector3 TrackedPosition;
    }
}
