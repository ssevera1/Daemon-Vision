# Building D-Space from Source

## Prerequisites

- **Unity 2022.3 LTS** or newer (2023.x recommended)
- **Android Build Support** module (via Unity Hub)
- **Android SDK 28+** and **NDK r25+**
- **JDK 11**
- **Git** for version control
- **ADB** for device deployment

### Optional
- **Xcode 15+** for iOS builds
- **Meta XR SDK** for Quest-specific features
- **Snapdragon Spaces SDK** for Qualcomm XR2 devices

---

## Setup

### 1. Clone Repository
```bash
git clone https://github.com/your-org/daemon-vision.git
cd daemon-vision
```

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
Builds appear in `unity-project/Builds/`:
- `DSpace_Quest.apk`
- `DSpace_AndroidXR.apk`
- `DSpace_PhoneAR.apk`
- `DSpace_iOS/` (Xcode project)

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
