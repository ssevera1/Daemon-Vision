# Building D-Space from Source

## Prerequisites

- **Unity 2022.3 LTS** (2022.3.62f1 is pinned in `ProjectSettings/ProjectVersion.txt`)
- **Android Build Support** module (via Unity Hub)
- **Android SDK 28+** and **NDK r25+**
- **JDK 17** (required by Android Gradle Plugin 8 for the companion app)
- **Git** for version control
- **ADB** for device deployment

### Optional
- **Xcode 15+** for iOS builds
- **Meta XR SDK** for Quest-specific features
- **Snapdragon Spaces SDK** for Qualcomm XR2 devices
- **Unity Sentis 2.x** for ML person detection. Sentis 2.x requires Unity 6, so on
  2022.3 the `MLPersonDetector` subsystem compiles to a stub and the Editor uses
  the simulated detector instead. To enable it: open the project in Unity 6, add
  `com.unity.sentis` 2.1.x in Package Manager (the `UNITY_SENTIS` define is set
  automatically by `DaemonVision.asmdef`), import an ONNX model, and assign it to
  the `MLPersonDetector` component's Model Asset field.

---

## Setup

### 1. Clone Repository
```bash
git clone https://github.com/ssevera1/Daemon-Vision.git
cd Daemon-Vision
```

Unity `.meta` files are committed. If you add assets outside the Editor, run
`python tools/unity/generate_meta.py` before committing so scene references
keep their GUIDs.

### 2. Open in Unity
1. Open Unity Hub → Add Project → Select `unity-project/` directory
2. Unity will import packages from `Packages/manifest.json`
3. Wait for compilation (first time takes a few minutes)

### 3. Configure XR
1. Edit → Project Settings → XR Plug-in Management
2. Enable the appropriate XR backend:
   - **Meta Quest**: Oculus XR Plugin
   - **Android XR**: OpenXR
   - **Phone AR**: ARCore/ARKit

### 4. Configure Signing (Android)
1. Edit → Project Settings → Player → Android → Publishing Settings
2. Create or select a keystore for signing
3. Set Key Alias and passwords

---

## Building

### From Unity Editor
Use the menu: **DaemonVision → Build → [Target Platform]**

Available targets:
- Meta Quest 3 (APK)
- Android XR Glasses (APK)
- Android Phone AR (APK)
- iOS (Xcode Project)

### From Command Line
```bash
# Meta Quest build
Unity -batchmode -projectPath ./unity-project \
  -executeMethod DaemonVision.Editor.DaemonVisionBuildConfig.BuildMetaQuest \
  -quit -logFile build.log

# Android XR build
Unity -batchmode -projectPath ./unity-project \
  -executeMethod DaemonVision.Editor.DaemonVisionBuildConfig.BuildAndroidXR \
  -quit -logFile build.log
```

### Output
Editor menu builds appear in `unity-project/Builds/`:
- `DSpace_Quest.apk`
- `DSpace_AndroidXR.apk`
- `DSpace_PhoneAR.apk`
- `DSpace_iOS/` (Xcode project)

`tools/build/build.sh` passes `-outputPath` and writes timestamped APKs under
`builds/<target>/` at the repository root, plus a `*_latest.apk` copy that
`tools/build/deploy.sh` installs. Set `BUILD_CONFIG=release` for a
non-development build.

---

## Network Ports

All transports are UDP on the local network. The companion app and the Unity
app share these constants (`tools/ci/validate_project.py` fails CI if they
drift apart).

| Port | Direction | Purpose | Defined in |
|------|-----------|---------|------------|
| 7733 | glasses to glasses | Mesh messages (chat, heartbeats, identity, quests) | `MeshNetworkManager.DefaultMeshPort` |
| 7734 | broadcast | Discovery beacons `DSPACE:{json}`; the companion app listens here to find the glasses | `PeerDiscovery.DefaultDiscoveryPort`, `RelayProtocol.DISCOVERY_PORT` |
| 7735 | phone to glasses | GPS relay `DSPACE_GPS\|lat\|lon\|alt\|acc\|bearing\|unixMillis`, answered with `DSPACE_ACK\|peers\|unixMillis` | `CompanionLocationReceiver.DefaultPort`, `RelayProtocol.GPS_RELAY_PORT` |

---

## Development Workflow

### Scene Setup
1. Open `Assets/Scenes/DSpaceMain.unity`
2. The scene contains a root `DaemonVision` GameObject with:
   - `DarknetBootstrap` — auto-registers all subsystems
   - `DSpaceManager` — central coordinator
   - `ServiceLocator` — dependency injection
3. AR Foundation components (ARSession, ARSessionOrigin, etc.) are on child objects

### Testing in Editor
1. D-Space runs in simulation mode in the Unity Editor
2. GPS is simulated via `DSpaceConfig.SimulatedLatitude/Longitude`
3. Set `DSpaceConfig.SpawnTestOperatives = true` for simulated people
4. Use Game view + Scene view simultaneously to see both perspectives

### Testing on Device
```bash
# Build and deploy in one step
adb install -r Builds/DSpace_Quest.apk

# View logs
adb logcat -s Unity DSpace
```

---

## Project Structure

```
unity-project/Assets/
├── Scripts/
│   ├── Core/          — DSpaceManager, ServiceLocator, Bootstrap
│   ├── Identity/      — DarknetIdentity, BiometricAuth, Callsigns
│   ├── Spatial/       — GPS anchoring, world mesh, spatial awareness
│   ├── HUD/           — Nameplates, compass, status bar, minimap, quest HUD
│   ├── Social/        — Reputation, factions, classes, leveling
│   ├── Quest/         — Quest system, objectives, rewards
│   ├── Economy/       — Darknet credits, transactions
│   ├── Network/       — Mesh networking, peer discovery, encryption
│   ├── Detection/     — Person detection, threat assessment
│   ├── Communication/ — Chat, voice channels
│   ├── Input/         — Gaze, gestures, voice commands
│   └── Config/        — DSpaceConfig, GlassesProfiles
├── Shaders/           — Holographic, nameplate, threat, quest path, grid
├── Editor/            — Build configurations
├── Scenes/            — DSpaceMain, Calibration
└── Resources/Config/  — Runtime-loaded configuration assets
```

---

## Architecture

The app uses a **subsystem architecture** inspired by the Daemon's modular design:

1. `DarknetBootstrap` registers all subsystems with `DSpaceManager`
2. `DSpaceManager` initializes them in dependency order
3. Each subsystem implements `IDSpaceSubsystem` with lifecycle methods
4. Subsystems communicate via events and `GetSubsystem<T>()` queries
5. `ServiceLocator` provides core service injection

This allows any subsystem to be enabled/disabled independently — critical for supporting devices with different capabilities.
