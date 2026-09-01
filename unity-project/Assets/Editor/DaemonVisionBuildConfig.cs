// DaemonVisionBuildConfig.cs - Build automation for the supported AR glasses targets
// Menu items for interactive builds, plus the static entry points that
// tools/build/build.sh invokes with -executeMethod. Command-line builds honour
// "-outputPath <file>" and the BUILD_CONFIG environment variable
// ("release" or "debug").

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DaemonVision.Editor
{
    public static class DaemonVisionBuildConfig
    {
        private const string CompanyName = "DaemonVision";
        private const string ProductName = "D-Space";

        // Must match tools/build/deploy.sh and ProjectSettings.asset.
        public const string BundleIdentifier = "com.daemon.vision.dspace";

        private const string DefaultBuildDir = "Builds";

        private static readonly string[] TargetDefines =
        {
            "DSPACE_QUEST", "DSPACE_ANDROIDXR", "DSPACE_PHONE", "DSPACE_IOS"
        };

        [MenuItem("DaemonVision/Build/Meta Quest 3 (APK)")]
        public static void BuildMetaQuest()
        {
            // Meta requires API 29+ for Quest store and sideload builds.
            BuildAndroid("DSpace_Quest.apk", AndroidSdkVersions.AndroidApiLevel29, "Meta Quest 3", "DSPACE_QUEST");
        }

        [MenuItem("DaemonVision/Build/Android XR Glasses (APK)")]
        public static void BuildAndroidXR()
        {
            BuildAndroid("DSpace_AndroidXR.apk", AndroidSdkVersions.AndroidApiLevel28, "Android XR", "DSPACE_ANDROIDXR");
        }

        [MenuItem("DaemonVision/Build/Android Phone AR (APK)")]
        public static void BuildPhoneAR()
        {
            BuildAndroid("DSpace_PhoneAR.apk", AndroidSdkVersions.AndroidApiLevel28, "Phone AR", "DSPACE_PHONE");
        }

        [MenuItem("DaemonVision/Build/iOS (Xcode Project)")]
        public static void BuildIOS()
        {
            SetCommonSettings();
            SetTargetDefine(BuildTargetGroup.iOS, "DSPACE_IOS");

            string output = ResolveOutputPath(Path.Combine(DefaultBuildDir, "DSpace_iOS"));
            Run(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = output,
                target = BuildTarget.iOS,
                options = ResolveBuildOptions()
            }, "iOS");
        }

        private static void BuildAndroid(string defaultFileName, AndroidSdkVersions minSdk, string label, string define)
        {
            SetCommonSettings();
            PlayerSettings.Android.minSdkVersion = minSdk;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            SetTargetDefine(BuildTargetGroup.Android, define);

            string output = ResolveOutputPath(Path.Combine(DefaultBuildDir, defaultFileName));
            Run(new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = output,
                target = BuildTarget.Android,
                options = ResolveBuildOptions()
            }, label);
        }

        private static void SetCommonSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleIdentifier);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleIdentifier);
        }

        /// <summary>
        /// Replace any DSPACE_* define with the one for this target so per-device
        /// code paths (#if DSPACE_QUEST) are exclusive.
        /// </summary>
        private static void SetTargetDefine(BuildTargetGroup group, string define)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(group);
            string existing = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = new List<string>();
            foreach (var d in existing.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Array.IndexOf(TargetDefines, d) < 0)
                    defines.Add(d);
            }
            defines.Add(define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        private static string[] GetScenes()
        {
            return new[]
            {
                "Assets/Scenes/DSpaceMain.unity",
                "Assets/Scenes/Calibration.unity"
            };
        }

        /// <summary>
        /// "-outputPath <path>" from the command line wins; otherwise the default
        /// under Builds/. The parent directory is created either way.
        /// </summary>
        private static string ResolveOutputPath(string defaultPath)
        {
            string path = defaultPath;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-outputPath", StringComparison.OrdinalIgnoreCase))
                {
                    path = args[i + 1];
                    break;
                }
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return path;
        }

        private static BuildOptions ResolveBuildOptions()
        {
            string config = Environment.GetEnvironmentVariable("BUILD_CONFIG") ?? "debug";
            return config.Equals("release", StringComparison.OrdinalIgnoreCase)
                ? BuildOptions.None
                : BuildOptions.Development;
        }

        private static void Run(BuildPlayerOptions options, string label)
        {
            Debug.Log($"[DaemonVision] Building {label} -> {options.locationPathName} ({options.options})");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DaemonVision] {label} build succeeded. Size: {report.summary.totalSize / (1024 * 1024)} MB");
                return;
            }

            Debug.LogError($"[DaemonVision] {label} build failed: {report.summary.result} " +
                           $"({report.summary.totalErrors} errors)");

            // In -batchmode a non-zero exit code is the only signal build.sh can see.
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
