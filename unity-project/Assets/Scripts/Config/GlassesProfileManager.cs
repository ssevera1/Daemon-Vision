// GlassesProfileManager.cs — Hardware profiles for different AR glasses
// Each pair of glasses has different capabilities. This manager detects
// the device and loads the appropriate profile for optimal D-Space rendering.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Core
{
    public class GlassesProfileManager : SubsystemBase
    {
        public override string Name => "GlassesProfile";

        public GlassesProfile ActiveProfile { get; private set; }

        private readonly Dictionary<string, GlassesProfile> profiles
            = new Dictionary<string, GlassesProfile>();

        protected override Task OnInitialize()
        {
            RegisterProfiles();
            DetectAndLoadProfile();
            return Task.CompletedTask;
        }

        private void RegisterProfiles()
        {
            // Meta Quest 3 / 3S
            profiles["quest3"] = new GlassesProfile
            {
                Id = "quest3",
                DeviceName = "Meta Quest 3",
                DisplayType = DisplayType.VideoPassthrough,
                FieldOfView = 104f,
                HasCamera = true,
                HasDepthSensor = true,
                HasGPS = false,
                HasHandTracking = true,
                HasEyeTracking = true,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "OpenXR", "OculusXR", "ARFoundation" },
                RenderScale = 1.0f,
                RecommendedFPS = 72,
                Notes = "Best standalone option. Requires phone tethering for GPS."
            };

            // Samsung Galaxy XR
            profiles["galaxy_xr"] = new GlassesProfile
            {
                Id = "galaxy_xr",
                DeviceName = "Samsung Galaxy XR",
                DisplayType = DisplayType.VideoPassthrough,
                FieldOfView = 109f,
                HasCamera = true,
                HasDepthSensor = true,
                HasGPS = true, // Android XR with ARCore Geospatial
                HasHandTracking = true,
                HasEyeTracking = true,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "AndroidXR", "ARCore", "OpenXR" },
                RenderScale = 1.0f,
                RecommendedFPS = 90,
                Notes = "Best D-Space experience. Full ARCore Geospatial API for GPS anchoring."
            };

            // Magic Leap 2
            profiles["magic_leap_2"] = new GlassesProfile
            {
                Id = "magic_leap_2",
                DeviceName = "Magic Leap 2",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 70f,
                HasCamera = true,
                HasDepthSensor = true,
                HasGPS = false,
                HasHandTracking = true,
                HasEyeTracking = true,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "OpenXR", "MagicLeapSDK" },
                RenderScale = 1.0f,
                RecommendedFPS = 60,
                Notes = "Best optical see-through. True AR transparency. Enterprise pricing."
            };

            // XREAL Air 2 Ultra
            profiles["xreal_air2_ultra"] = new GlassesProfile
            {
                Id = "xreal_air2_ultra",
                DeviceName = "XREAL Air 2 Ultra",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 52f,
                HasCamera = false, // No RGB camera
                HasDepthSensor = true, // Dual depth cameras for SLAM
                HasGPS = false, // Tethered to phone
                HasHandTracking = false,
                HasEyeTracking = false,
                HasMicrophone = false,
                HasSpeaker = true,
                SupportedSDKs = new[] { "XREAL_SDK", "Nebula" },
                RenderScale = 1.0f,
                RecommendedFPS = 60,
                Notes = "Tethered display. Use phone camera and GPS. No onboard face detection."
            };

            // RayNeo X2
            profiles["rayneo_x2"] = new GlassesProfile
            {
                Id = "rayneo_x2",
                DeviceName = "RayNeo X2",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 25f,
                HasCamera = true, // 16MP camera
                HasDepthSensor = false,
                HasGPS = true,
                HasHandTracking = false,
                HasEyeTracking = false,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "Android_AOSP" },
                RenderScale = 0.8f,
                RecommendedFPS = 30,
                Notes = "Standalone Android. Narrow FOV limits D-Space visibility. Good for HUD-only mode."
            };

            // Rokid AR Lite
            profiles["rokid_ar_lite"] = new GlassesProfile
            {
                Id = "rokid_ar_lite",
                DeviceName = "Rokid AR Lite",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 50f,
                HasCamera = true,
                HasDepthSensor = false,
                HasGPS = false, // Compute puck may have GPS
                HasHandTracking = false,
                HasEyeTracking = false,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "Android_AOSP", "RokidSDK" },
                RenderScale = 0.9f,
                RecommendedFPS = 60,
                Notes = "Android compute puck. Good 90% Android APK compatibility."
            };

            // Vuzix Shield
            profiles["vuzix_shield"] = new GlassesProfile
            {
                Id = "vuzix_shield",
                DeviceName = "Vuzix Shield",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 28f,
                HasCamera = true, // Stereo 13MP
                HasDepthSensor = false,
                HasGPS = true,
                HasHandTracking = false,
                HasEyeTracking = false,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "Android_AOSP", "VuzixSDK" },
                RenderScale = 0.7f,
                RecommendedFPS = 30,
                Notes = "Enterprise-focused. Good cameras for detection. Narrow FOV."
            };

            // Android XR Glasses (2026 reference design)
            profiles["android_xr_glasses"] = new GlassesProfile
            {
                Id = "android_xr_glasses",
                DeviceName = "Android XR Glasses",
                DisplayType = DisplayType.OpticalSeeThrough,
                FieldOfView = 70f,
                HasCamera = true,
                HasDepthSensor = true,
                HasGPS = true,
                HasHandTracking = true,
                HasEyeTracking = true,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "AndroidXR", "ARCore", "OpenXR" },
                RenderScale = 1.0f,
                RecommendedFPS = 90,
                Notes = "Future target platform. Full Android XR SDK + ARCore Geospatial API."
            };

            // Fallback: Phone AR (ARCore/ARKit)
            profiles["phone_ar"] = new GlassesProfile
            {
                Id = "phone_ar",
                DeviceName = "Phone AR (Handheld)",
                DisplayType = DisplayType.PhoneAR,
                FieldOfView = 60f,
                HasCamera = true,
                HasDepthSensor = false,
                HasGPS = true,
                HasHandTracking = false,
                HasEyeTracking = false,
                HasMicrophone = true,
                HasSpeaker = true,
                SupportedSDKs = new[] { "ARCore", "ARKit", "ARFoundation" },
                RenderScale = 1.0f,
                RecommendedFPS = 60,
                Notes = "Fallback for phone-based AR. Full GPS and camera access."
            };
        }

        private void DetectAndLoadProfile()
        {
            string profileId = DetectDevice();
            if (profiles.TryGetValue(profileId, out var profile))
            {
                ActiveProfile = profile;
                Log($"Loaded profile: {profile.DeviceName} (FOV: {profile.FieldOfView}°)");
            }
            else
            {
                ActiveProfile = profiles["phone_ar"]; // Safe fallback
                Log("Unknown device. Using phone AR fallback profile.");
            }
        }

        private string DetectDevice()
        {
            string model = SystemInfo.deviceModel.ToLower();
            string name = SystemInfo.deviceName.ToLower();

            // Detection heuristics
            if (model.Contains("quest") || name.Contains("quest"))
                return "quest3";
            if (model.Contains("samsung") && model.Contains("xr"))
                return "galaxy_xr";
            if (model.Contains("magic leap") || model.Contains("ml2"))
                return "magic_leap_2";
            if (model.Contains("xreal") || name.Contains("nebula"))
                return "xreal_air2_ultra";
            if (model.Contains("rayneo"))
                return "rayneo_x2";
            if (model.Contains("rokid"))
                return "rokid_ar_lite";
            if (model.Contains("vuzix"))
                return "vuzix_shield";

            // Check for XR subsystem
#if UNITY_2020_1_OR_NEWER
            var xrDisplaySubsystems = new List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(xrDisplaySubsystems);
            if (xrDisplaySubsystems.Count > 0)
                return "quest3"; // Generic XR headset, use Quest profile
#endif

            return "phone_ar";
        }

        public GlassesProfile GetProfile(string profileId)
        {
            profiles.TryGetValue(profileId, out var profile);
            return profile;
        }

        public IEnumerable<GlassesProfile> GetAllProfiles() => profiles.Values;
    }

    [System.Serializable]
    public class GlassesProfile
    {
        public string Id;
        public string DeviceName;
        public DisplayType DisplayType;
        public float FieldOfView;
        public bool HasCamera;
        public bool HasDepthSensor;
        public bool HasGPS;
        public bool HasHandTracking;
        public bool HasEyeTracking;
        public bool HasMicrophone;
        public bool HasSpeaker;
        public string[] SupportedSDKs;
        public float RenderScale;
        public int RecommendedFPS;
        public string Notes;
    }

    public enum DisplayType
    {
        VideoPassthrough,      // Quest, Galaxy XR — camera feed + overlays
        OpticalSeeThrough,    // Magic Leap, XREAL — see real world directly
        PhoneAR               // Phone screen with camera passthrough
    }
}
