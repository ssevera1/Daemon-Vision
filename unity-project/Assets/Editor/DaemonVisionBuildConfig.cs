// DaemonVisionBuildConfig.cs — Build automation for different AR glasses targets
// Provides menu items to build for each supported platform.

using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DaemonVision.Editor
{
    public class DaemonVisionBuildConfig
    {
        private const string CompanyName = "DaemonVision";
        private const string ProductName = "D-Space";
        private const string BundleIdentifier = "com.daemonvision.dspace";

        [MenuItem("DaemonVision/Build/Meta Quest 3 (APK)")]
        public static void BuildMetaQuest()
        {
            SetCommonSettings();
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            // Quest-specific XR settings
            PlayerSettings.SetVirtualRealitySupported(BuildTargetGroup.Android, true);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = "Builds/DSpace_Quest.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            LogBuildResult(report, "Meta Quest 3");
        }

        [MenuItem("DaemonVision/Build/Android XR Glasses (APK)")]
        public static void BuildAndroidXR()
        {
            SetCommonSettings();
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = "Builds/DSpace_AndroidXR.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            LogBuildResult(report, "Android XR");
        }

        [MenuItem("DaemonVision/Build/Android Phone AR (APK)")]
        public static void BuildPhoneAR()
        {
            SetCommonSettings();
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = "Builds/DSpace_PhoneAR.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            LogBuildResult(report, "Phone AR");
        }

        [MenuItem("DaemonVision/Build/iOS (Xcode Project)")]
        public static void BuildIOS()
        {
            SetCommonSettings();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = "Builds/DSpace_iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.None
            });

            LogBuildResult(report, "iOS");
        }

        private static void SetCommonSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleIdentifier);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleIdentifier);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }

        private static string[] GetScenes()
        {
            return new[]
            {
                "Assets/Scenes/DSpaceMain.unity",
                "Assets/Scenes/Calibration.unity"
            };
        }

        private static void LogBuildResult(BuildReport report, string target)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DaemonVision] {target} build succeeded! Size: {report.summary.totalSize / (1024 * 1024)}MB");
            }
            else
            {
                Debug.LogError($"[DaemonVision] {target} build failed: {report.summary.result}");
            }
        }
    }
}
