# AR Glasses & Smart Glasses Research - March 2026

## Comprehensive Guide: Sideloading, SDKs, Sensors, and Development Platforms

---

## 1. DEVICES THAT SUPPORT SIDELOADING

---

### 1.1 Meta Quest 3 / Quest 3S / Quest Pro

**OS/Platform:** Meta Horizon OS (Android-based, AOSP derivative), Qualcomm Snapdragon XR2 Gen 2, 8 GB RAM

**How to Enable Developer Mode & Sideload:**
- Create a free Meta developer account at developer.meta.com
- Open Meta Quest mobile app > Menu > Devices > select headset > Developer Mode > toggle ON
- Connect via USB-C, install ADB platform tools or use SideQuest
- Drag and drop .apk files in SideQuest, or use `adb install <path>.apk`
- Sideloading is officially supported and legal; Meta encourages it for developers

**Available SDKs:**
- **Meta OpenXR SDK** (primary): Full OpenXR support for native C/C++ development
- **Meta XR SDK for Unity**: Includes AR Foundation integration, passthrough, hand tracking, spatial anchors
- **Meta XR SDK for Unreal**: OpenXR-based plugin for UE5
- **Passthrough Camera API**: Access to front camera frames via Android Camera2 API (experimental, becoming stable)
- **WebXR**: Full support in Meta Quest Browser (immersive-ar mode, plane detection, anchors, hand tracking, hit testing)
- No native ARCore support, but AR Foundation abstracts much of this

**Camera/Sensor Capabilities:**
- 2x front-facing IR cameras + 2x side IR cameras (inside-out 6DoF tracking)
- 4 MP RGB color passthrough cameras (video see-through mixed reality)
- IR structured light depth projector (center pill) for depth estimation
- IMU sensors in headset and controllers
- Hand tracking via onboard cameras (no controllers required)
- **No GPS** - requires phone tethering or WiFi-based location

**Display Type:** Binocular LCD, pancake lens optics (video passthrough, NOT optical see-through)

**Field of View:** ~104° horizontal (VR), passthrough FOV slightly narrower

**World-Anchored AR Content:**
- Yes - Spatial Anchors API, Scene Understanding API, plane detection, mesh generation
- Persistent local anchors supported
- No native GPS-based geospatial anchoring (would need custom implementation)

**GPS / Location:**
- No onboard GPS
- Can tether to phone via companion app or custom networking for location data
- WiFi-based approximate positioning possible

**Networking:**
- WiFi 6E (6 GHz), Bluetooth 5.3
- WiFi Direct: Not officially documented in SDK, but Android WiFi Direct APIs may be accessible
- Standard TCP/UDP networking over WiFi

**Best For:** Most mature mixed reality development platform; excellent for indoor AR with passthrough; large developer community; weakest for GPS-anchored outdoor AR

---

### 1.2 Samsung Galaxy XR

**OS/Platform:** Android XR (Google's new XR-specific Android variant), Qualcomm Snapdragon XR2+ Gen 2, 16 GB RAM

**How to Enable Developer Mode & Sideload:**
- **Sideloading does NOT require Developer Mode** - download APK in Chrome browser, grant "unknown apps" permission, install directly
- Developer Mode available via Settings > About > tap Build Number 7 times (standard Android)
- ADB sideloading also supported
- **Bootloader is unlockable** for custom ROM development

**Available SDKs:**
- **Android XR SDK** (Jetpack XR): Google's first-party XR development framework
- **ARCore for Jetpack XR**: Motion tracking, plane detection, spatial anchors, **Geospatial API**
- **Unity with OpenXR**: Full support via Unity Android XR package
- **Unreal Engine with OpenXR**: Supported
- **WebXR**: Full support in Chrome (immersive-ar, immersive-vr)
- **Jetpack Compose for XR**: Native Android UI development
- Standard Android APIs accessible

**Camera/Sensor Capabilities:**
- Multiple passthrough cameras for video see-through mixed reality
- Depth sensing capabilities
- Hand tracking support
- Eye tracking
- IMU sensors
- **No onboard GPS confirmed** (relies on phone companion or WiFi positioning)

**Display Type:** Binocular micro-OLED, 3552x3840 per eye (29M pixels), 96% DCI-P3, pancake optics (video passthrough)

**Field of View:** 109°

**World-Anchored AR Content:**
- Yes - ARCore Geospatial API supported (WGS84, Terrain, and Rooftop anchors)
- Spatial anchors, plane detection, scene mesh
- Persistent cloud anchors via Google Cloud Anchor service

**GPS / Location:**
- Geospatial API uses Visual Positioning Service (VPS) + device sensors
- Phone companion connectivity for GPS data

**Networking:** WiFi 6E, Bluetooth 5.3

**Best For:** Most open Android-based headset; best geospatial AR anchoring via ARCore; strong for developers already in Android ecosystem. However, it's a headset, not glasses.

---

### 1.3 XREAL Air 2 Ultra

**OS/Platform:** Tethered display - no onboard OS. Requires compatible Android phone (Samsung Galaxy S22/S23/S24 flagship series) running XREAL Nebula app. Spatial computing runs on the phone's processor.

**How to Enable Developer Mode & Sideload:**
- Enable Developer Options + USB Debugging on the connected Android phone
- Build APK using XREAL SDK (Unity-based), deploy to phone via `adb install`
- WiFi ADB recommended for untethered debugging while glasses are connected via USB-C to phone
- Apps run on the phone; glasses serve as the 6DoF spatial display

**Available SDKs:**
- **XREAL SDK 3.0.0** (formerly NRSDK): Major rewrite, fully integrated with Unity XR ecosystem
- Supports: Motion tracking (6DoF via onboard sensors), plane detection, image anchoring, hand tracking, mesh generation
- Unity AR Foundation compatible
- **No Snapdragon Spaces support** on Air 2 Ultra specifically

**Camera/Sensor Capabilities:**
- Dual 3D environment sensors (stereo depth cameras) for 6DoF SLAM tracking
- Real-time mesh generation of environment
- Semantic scene understanding (classifies surfaces)
- Hand tracking via depth cameras
- **No RGB photo/video camera** - depth sensors only
- IMU sensors onboard

**Display Type:** Binocular micro-OLED (Sony), optical see-through with waveguide, 1080p per eye, 120 Hz

**Field of View:** 52°

**World-Anchored AR Content:**
- Yes - 6DoF tracking with SLAM, plane detection, spatial anchors
- No native GPS-based geospatial anchoring (would need phone GPS + custom implementation)

**GPS / Location:** Via tethered phone's GPS

**Networking:** Via tethered phone (WiFi, Bluetooth, cellular)

**Best For:** Lightweight optical see-through AR glasses with real spatial computing; good 6DoF tracking; limited by phone tethering requirement and Samsung-only compatibility

---

### 1.4 XREAL One / One Pro

**OS/Platform:** Tethered display with onboard X1 spatial co-processing chip. Connects to phones, PCs, consoles, or gaming handhelds via USB-C. The X1 chip handles spatial computing independently.

**How to Enable Developer Mode & Sideload:**
- Similar to Air 2 Ultra workflow - apps deploy to the connected host device
- XREAL SDK 3.0.0 used for development in Unity
- Currently limited phone support (Samsung S24 and later flagships for Android phone development)

**Available SDKs:**
- XREAL SDK 3.0.0 with Unity XR integration
- Spatial computing features handled by onboard X1 chip (3ms motion-to-photon latency at 120Hz)

**Camera/Sensor Capabilities:**
- Spatial sensors for 3DoF/6DoF tracking (depending on connected device capabilities)
- IMU sensors
- **No depth cameras** (unlike Air 2 Ultra) - primarily a display device with spatial awareness
- No RGB camera

**Display Type:** Binocular Sony micro-OLED, optical see-through, 0.55", 1080p per eye, 120 Hz

**Field of View:** 57°

**World-Anchored AR Content:** Limited compared to Air 2 Ultra - primarily 3DoF spatial screen positioning; 6DoF requires compatible phone with XREAL Nebula

**GPS / Location:** Via connected device

**Best For:** Consumer media viewing with spatial awareness; less suitable for full AR development than Air 2 Ultra

---

### 1.5 XREAL Project Aura (Coming 2026)

**OS/Platform:** Android XR via tethered compute puck with Qualcomm Snapdragon chip + onboard X1S chip

**Key Specs (Announced):**
- 70°+ field of view
- Dual-chip design (X1S + Snapdragon)
- Android XR support confirmed
- Will support ARCore Geospatial API and full Android XR SDK
- Google XR Glasses emulator support in Android Studio

**Best For:** Potentially the best upcoming optical see-through AR glasses platform with full Android XR + geospatial support

---

### 1.6 RayNeo X2

**OS/Platform:** RayNeo AIOS (Android AOSP-based), Qualcomm Snapdragon XR2, 6 GB RAM, 128 GB storage. Standalone device.

**How to Enable Developer Mode & Sideload:**
- Connect to PC via USB, use ADB
- `adb install <path>.apk` to install applications
- On firmware v1.2.66+, must first run: `adb shell settings put global mercury_install_allowed 1`
- RayNeo developer portal at open.rayneo.com for SDK access
- Part of "The Morpheus Plan" global developer program

**Available SDKs:**
- RayNeo proprietary SDK (Android + Unity based)
- Snapdragon Spaces SDK compatible
- Standard Android development tools

**Camera/Sensor Capabilities:**
- 16 MP front-facing RGB camera with anti-shake stabilization
- SLAM tracking sensors
- IMU sensors
- Microphone array

**Display Type:** Binocular full-color micro-LED waveguide, optical see-through, 1000-1500 nits brightness, 100,000:1 contrast

**Field of View:** 25°

**World-Anchored AR Content:**
- Basic SLAM-based spatial anchoring
- Limited by narrow FOV

**GPS / Location:** Onboard GPS likely available (standalone Android device with cellular/WiFi)

**Networking:** WiFi 5, Bluetooth 5.2

**Best For:** Standalone AR glasses with camera; narrow FOV limits utility for overlay applications

---

### 1.7 RayNeo X3 Pro

**OS/Platform:** RayNeo AIOS (Android-based), standalone with onboard compute

**How to Enable Developer Mode & Sideload:**
- Similar ADB-based sideloading as X2
- RayNeo developer portal for SDK access

**Available SDKs:**
- RayNeo SDK
- Qualcomm-based development tools
- Get Started guide available via Qualcomm developer portal

**Camera/Sensor Capabilities:**
- Sony IMX681 RGB sensor: 12 MP wide-angle photos, 4K/3K video
- OV spatial camera for depth sensing and SLAM mapping
- Dual-camera system (RayNeo Image Plus)
- IMU, microphone

**Display Type:** Binocular color micro-LED waveguide, 640x480, optical see-through, 6000 nits peak brightness, 60 Hz

**Field of View:** 30°

**World-Anchored AR Content:**
- Depth + SLAM-based spatial anchoring
- Better than X2 due to dedicated depth camera

**GPS / Location:** Standalone device - likely onboard GPS/WiFi positioning

**Networking:** WiFi, Bluetooth

**Best For:** Improved over X2 with depth camera; still narrow FOV; battery life reported as problematic (~2 hours)

---

### 1.8 Vuzix Shield

**OS/Platform:** Android 11 (API 30), 8-core CPU, standalone device

**How to Enable Developer Mode & Sideload:**
- Standard Android developer mode (Settings > About > tap Build Number 7 times)
- ADB sideloading supported
- Vuzix developer agreement required for SDK access

**Available SDKs:**
- Vuzix Shield Speech Command SDK
- Vuzix Shield Connectivity SDK (connects to companion Android/iOS phone)
- Standard Android SDK development
- Vuzix Developer Center with samples and documentation

**Camera/Sensor Capabilities:**
- Stereo 13 MP cameras with autofocus, 4K 30fps video
- IMU sensors
- Microphone
- No dedicated depth sensor

**Display Type:** Binocular waveguide with micro-LED projectors (1 micron LEDs), optical see-through, non-occluded display

**Field of View:** ~20-28° (estimated; exact FOV not prominently documented)

**World-Anchored AR Content:**
- Limited - primarily an enterprise notification/information overlay device
- No advanced SLAM or spatial anchoring documented

**GPS / Location:** Via companion phone or onboard WiFi positioning

**Networking:** WiFi, Bluetooth

**Best For:** Enterprise/industrial use cases; rugged safety-rated design; limited for consumer AR overlay apps

---

### 1.9 Vuzix Z100 (Ultralite Platform)

**OS/Platform:** Not standalone - pairs via Bluetooth to Android or iOS phone running Vuzix Connect app. Display-only smart glasses.

**How to Enable Developer Mode & Sideload:**
- Development is on the companion phone, not the glasses themselves
- Vuzix Ultralite SDK for Android and iOS
- Apps push content (text, images, notifications) to glasses display

**Available SDKs:**
- Vuzix Ultralite SDK (Android + iOS)
- Vuzix Connect demo apps and sample code on GitHub

**Camera/Sensor Capabilities:**
- No cameras
- Basic IMU/head motion sensors
- Microphone

**Display Type:** Monocular waveguide, monochrome, optical see-through

**Field of View:** ~30°

**World-Anchored AR Content:** No - notification/HUD display only

**GPS / Location:** Via paired phone

**Networking:** Bluetooth to phone

**Best For:** Lightweight (38g) all-day notification display; 48-hour battery; not suitable for AR overlay apps

---

### 1.10 Magic Leap 2

**OS/Platform:** Android 10 (AOSP, API 29), Qualcomm Snapdragon XR2 (custom variant), standalone device. Enterprise-focused.

**How to Enable Developer Mode & Sideload:**
- Settings > About > scroll to Build Number > tap 7 times (standard Android method)
- Use Magic Leap Hub desktop software for device management, APK installation, debugging
- Click "Install App" in Magic Leap Hub, browse to .APK file
- ADB sideloading also supported
- No built-in app store; all apps installed via Magic Leap Hub or ADB

**Available SDKs:**
- **Magic Leap Unity SDK** (primary): Full spatial computing features
- **Magic Leap OpenXR**: Native OpenXR support
- **Magic Leap AOSP Tools**: Standard Android debugging and profiling
- **Spatial Anchors API** with AR Cloud support
- Unity AR Foundation compatible
- Snapdragon Spaces SDK compatible

**Camera/Sensor Capabilities:**
- 3x forward-facing IR world cameras (center + 2 near temples) for SLAM/tracking
- 1x forward-facing RGB video camera (accessible via Android APIs + ML2 APIs)
- 1x Time-of-Flight depth camera (pmdtechnologies): 544x480 resolution, 75°(h) x 70°(v) FOV
- IMU sensors
- Eye tracking cameras
- Hand tracking support
- Microphone array

**Display Type:** Binocular waveguide, optical see-through, with dynamic dimming (industry-leading feature that can darken the real world behind virtual content)

**Field of View:** 70° diagonal

**World-Anchored AR Content:**
- Yes - Spatial Anchors API with persistent cloud anchors
- Full SLAM-based world meshing and scene understanding
- AR Cloud for shared/persistent spatial content
- Best-in-class spatial anchoring among optical see-through glasses

**GPS / Location:**
- No onboard GPS documented
- Primarily enterprise indoor use
- WiFi positioning possible; phone tethering for outdoor GPS

**Networking:** WiFi 6, Bluetooth 5.0

**Best For:** Most capable optical see-through AR headset available; excellent spatial anchoring and depth sensing; enterprise pricing ($3,299+); limited consumer availability

---

### 1.11 Rokid AR Lite (Max 2 + Station 2)

**OS/Platform:** Rokid custom Android skin on Station 2 compute unit, Qualcomm Snapdragon 6 Gen 1, 8 GB RAM, 128 GB storage

**How to Enable Developer Mode & Sideload:**
- Station 2 runs Android - standard APK installation via ADB or Android app markets (APKPure, Uptodown)
- ~90% of standard Android apps run normally
- Rokid UXR SDK for AR-specific development

**Available SDKs:**
- Rokid UXR SDK (Dock SDK for Rokid Station + glasses, Phone SDK for phone companion)
- Hand gesture recognition, voice input, projection features
- Unity development support
- 20,000+ registered developers; 200+ academic/industry projects

**Camera/Sensor Capabilities:**
- Station 2 has cameras/sensors for 3DoF tracking
- Max 2 glasses: primarily a display device with IMU
- Limited spatial sensing compared to competitors

**Display Type:** Binocular Sony micro-OLED (0.68"), optical see-through, birdbath optics, 600 nits, 90 Hz

**Field of View:** 50°

**World-Anchored AR Content:** Basic 3DoF spatial awareness via Station 2; limited world anchoring

**GPS / Location:** Station 2 has WiFi 6 + Bluetooth 5.2; GPS via companion phone

**Networking:** WiFi 6, Bluetooth 5.2 (Station 2)

---

### 1.12 Rokid Glasses (2025)

**OS/Platform:** Onboard Snapdragon AR1 Gen 1 chip, tethered to phone app for heavy AI compute

**Specs:** 49g, dual-eye micro-LED waveguide display, 23° FOV, 12 MP camera (Sony IMX681), 1500 nits, ~$600

**Developer:** Open SDK with 20,000+ developers; primarily an AI assistant/notification device with camera

**Best For:** Lightweight AI-first glasses with camera; too narrow FOV for AR overlay apps

---

### 1.13 Snap Spectacles (5th Gen - Coming 2026)

**OS/Platform:** Snap OS 2.0, Qualcomm Snapdragon processor, standalone AR glasses

**How to Develop:**
- Lens Studio 5.0+ for building Lenses (Snap's app format)
- Push projects directly to Spectacles from Lens Studio
- **Not traditional Android sideloading** - Snap's closed ecosystem with Lens-based development
- WebXR support in built-in browser (Snap OS 2.0)

**Available SDKs/APIs:**
- Lens Studio with Spectacles templates
- Depth Module API (3D anchoring from 2D LLM data)
- Automated Speech Recognition API (40+ languages)
- Snap3D API (real-time 3D object generation)
- OpenAI and Gemini integrations for multimodal AI
- Snap Cloud backend infrastructure

**Display Type:** Binocular waveguide, optical see-through, stereo display, adjustable tint

**Field of View:** 46°

**World-Anchored AR Content:** Spatial mapping supported; persistent Lenses at locations

**GPS / Location:** Standalone device - GPS likely onboard

**Networking:** WiFi, Bluetooth

**Best For:** Social AR experiences; strong AI integration; limited by Snap's closed ecosystem (no APK sideloading); WebXR provides an open development path

---

### 1.14 Even Realities G1

**OS/Platform:** Proprietary embedded OS; pairs via Bluetooth to iOS/Android phone

**Specs:** Monocular green monochrome display, 640x200, 25° FOV, 1000 nits, prescription-compatible

**Developer:** Even Hub developer portal; open SDK for sensor data, display, AI integration; GitHub repos available

**Best For:** Lightweight notification/teleprompter/translation glasses; no spatial AR capabilities

---

### 1.15 Google/Samsung Android XR Glasses (Coming Late 2026)

**OS/Platform:** Android XR (same platform as Galaxy XR headset)

**Key Details:**
- Partners: Samsung, Warby Parker, Gentle Monster, XREAL (Project Aura)
- Android XR SDK Developer Preview 3 released December 2025
- **Jetpack Projected** library: Extends mobile apps to glasses hardware
- **Jetpack Compose Glimmer**: UI toolkit for transparent displays
- **AI Glasses emulator** in Android Studio for development now
- ARCore Geospatial API will be supported
- Full developer ecosystem: Unity, Unreal, WebXR, native Android

**Best For:** THE platform to target for 2026-2027 if building optical see-through AR glasses apps with GPS anchoring

---

## 2. DEVELOPMENT FRAMEWORKS

---

### 2.1 Unity with AR Foundation / OpenXR

**Status:** Most mature cross-platform AR development framework

**Supported Devices:**
- Meta Quest 2/3/3S/Pro (via Meta OpenXR package)
- Samsung Galaxy XR (via Android XR OpenXR package)
- Magic Leap 2 (via Magic Leap Unity SDK)
- XREAL Air 2 Ultra / One (via XREAL SDK 3.0.0)
- HoloLens 2 (via Microsoft Mixed Reality OpenXR)
- Android XR glasses (upcoming, via Android XR OpenXR package)

**Key Features:**
- AR Foundation provides abstraction over platform-specific SDKs
- OpenXR ensures cross-platform compatibility
- Plane detection, image tracking, hand tracking, spatial anchors, mesh generation
- Mixed Reality example projects on GitHub

**Recommendation:** Best choice for cross-platform AR app targeting multiple headsets

---

### 2.2 Unreal Engine with OpenXR

**Status:** Strong VR support; AR glasses support improving

**Supported Devices:**
- Meta Quest series (OpenXR plugin)
- Samsung Galaxy XR (Android XR)
- Magic Leap 2

**Key Features:**
- High-fidelity rendering
- OpenXR plugin for device abstraction
- Passthrough support on Quest
- Less community support for AR glasses vs Unity

**Recommendation:** Better for graphically intensive VR/MR; Unity preferred for lightweight AR glasses apps

---

### 2.3 WebXR

**Status:** Rapidly maturing; browser support reaching critical mass

**Supported Devices & Browsers:**
- Meta Quest 3 (Quest Browser): Full immersive-ar support, plane detection, anchors, hand tracking
- Samsung Galaxy XR (Chrome): Full WebXR support
- Snap Spectacles (Snap OS 2.0 browser): WebXR support
- Apple Vision Pro (Safari): immersive-vr only (no immersive-ar yet)
- Android XR glasses (Chrome): Expected full support

**Development Frameworks:**
- Three.js + WebXR
- A-Frame
- Babylon.js
- PlayCanvas

**Key Advantage:** No sideloading needed; deploy via URL; works across devices

**Recommendation:** Best for prototyping and cross-device deployment; performance limited vs native

---

### 2.4 Android XR SDK (Jetpack XR)

**Status:** Developer Preview 3 (December 2025); first-party Google SDK

**Supported Devices:**
- Samsung Galaxy XR headset (available now)
- Android XR glasses from Samsung, XREAL, others (coming 2026)

**Key Libraries:**
- **ARCore for Jetpack XR**: Motion tracking, plane detection, spatial anchors, Geospatial API
- **Jetpack Projected**: Extend 2D Android apps to XR
- **Jetpack Compose Glimmer**: UI toolkit for transparent AR displays
- **AI Glasses emulator** in Android Studio

**Geospatial Features:**
- WGS84 anchors (GPS coordinates)
- Terrain anchors (ground-level placement)
- Rooftop anchors (building-top placement)
- Visual Positioning Service (VPS) for precise localization using Google Street View data

**Recommendation:** THE framework to invest in for 2026+ AR glasses development; strongest geospatial capabilities

---

### 2.5 Snapdragon Spaces SDK

**Status:** Available; Qualcomm's end-to-end AR platform built on OpenXR

**Supported Devices:**
- Lenovo ThinkReality A3 (paired with Motorola edge+)
- RayNeo X2
- TCL/DigiLens devices
- Lenovo ThinkReality VRX

**Key Features:**
- Dual Render Fusion (extend 2D mobile apps to 3D AR)
- Unity and Unreal Engine support
- Hand tracking, plane detection, spatial anchoring
- Phone-tethered architecture (phone does compute, glasses display)

**Recommendation:** Enterprise-focused; being eclipsed by Android XR SDK for consumer development

---

### 2.6 ARCore Geospatial API

**Status:** Production-ready; expanding to AR glasses via Android XR

**Key Capabilities:**
- Place AR content at GPS coordinates worldwide (87+ countries)
- WGS84 anchors: Latitude/longitude/altitude positioning
- Terrain anchors: Content placed relative to ground at GPS location
- Rooftop anchors: Content placed on building tops
- Visual Positioning Service: Camera-based precise localization using Google Street View imagery
- Cloud Anchors: Multi-user shared AR experiences

**Currently Supported On:**
- Android phones (primary platform)
- Samsung Galaxy XR headset
- Android XR glasses (coming 2026)

**Recommendation:** Best available solution for GPS-anchored persistent AR content

---

## 3. BEST TARGET PLATFORM ANALYSIS

### Requirements Assessment

| Requirement | Meta Quest 3 | Samsung Galaxy XR | Magic Leap 2 | XREAL Air 2 Ultra | Android XR Glasses (2026) |
|---|---|---|---|---|---|
| Camera passthrough / optical see-through | Video passthrough | Video passthrough | Optical see-through | Optical see-through | Optical see-through |
| GPS/location awareness | No onboard GPS | Via VPS/phone | No onboard GPS | Via phone | Expected onboard/VPS |
| World anchoring (GPS coords) | Custom only | ARCore Geospatial | Spatial Anchors (local) | Custom only | ARCore Geospatial |
| Face/person detection | Camera2 API access | Camera2 API access | RGB camera + APIs | No RGB camera | Expected camera APIs |
| Low-latency overlay | Good (~20ms) | Good | Excellent (optical) | Good (optical, 3ms M2P) | Expected good |
| Mesh networking | WiFi 6E, BT 5.3 | WiFi 6E, BT 5.3 | WiFi 6, BT 5.0 | Via phone | Expected WiFi 6E+ |
| Form factor | Bulky headset | Bulky headset | Large headset | Lightweight glasses | Lightweight glasses |
| Developer maturity | Excellent | Good (new) | Good (enterprise) | Moderate | Early (Preview) |
| Price | $500 | $1,800 | $3,300+ | $700 | TBD |

### Recommendations

#### Best Available NOW (Early 2026): Meta Quest 3

**Why:** Most mature development ecosystem, excellent passthrough cameras with Camera2 API access for face detection, large developer community, affordable, easy sideloading. Weaknesses: bulky headset form factor, no onboard GPS, video passthrough only (not optical see-through).

**Architecture:** Build on Quest using Unity + Meta OpenXR SDK + custom face detection (MediaPipe or on-device ML) + custom GPS relay from phone companion app via WiFi/Bluetooth.

#### Best Headset for GPS-Anchored AR NOW: Samsung Galaxy XR

**Why:** ARCore Geospatial API with WGS84/Terrain/Rooftop anchors, easy sideloading (no dev mode needed), full Android ecosystem, OpenXR. Weaknesses: expensive, bulky headset, newer platform.

**Architecture:** Android XR SDK + ARCore Geospatial API + Jetpack XR + standard Android Camera2 for face detection.

#### Best Optical See-Through Glasses NOW: Magic Leap 2

**Why:** 70° FOV (best in class), ToF depth sensor, RGB camera, excellent spatial anchoring, dynamic dimming. Weaknesses: enterprise pricing, no consumer availability, no GPS, large form factor for "glasses."

#### Best Platform to TARGET for 2026-2027: Android XR Glasses

**Why:** Google's full ecosystem backing, ARCore Geospatial API for GPS-anchored content, standard Android development, Unity/Unreal/WebXR support, multiple hardware partners (Samsung, XREAL Aura, Warby Parker). The XR Glasses emulator in Android Studio means you can start development NOW before hardware ships.

**Recommended Development Strategy:**
1. **Start now** with Android XR SDK Developer Preview 3 + AI Glasses emulator in Android Studio
2. **Prototype on** Samsung Galaxy XR headset (same Android XR platform, available now)
3. **Port to** XREAL Project Aura or Samsung AR glasses when hardware ships in late 2026
4. **Use ARCore Geospatial API** for GPS-anchored persistent AR objects
5. **Implement face detection** via Android Camera2 API + MediaPipe Face Detection or ML Kit
6. **Mesh networking** via Android WiFi Direct API (WifiP2pManager) + Bluetooth LE for device discovery

#### Dual-Target Strategy (Recommended):

**Primary:** Android XR (for GPS anchoring, optical see-through glasses in 2026)
**Secondary:** Meta Quest 3 (for immediate testing, large user base, passthrough AR)

Both support OpenXR, so a Unity AR Foundation project can target both with platform-specific plugins. The face detection and GPS relay components would be the most platform-specific code.

---

## 4. KEY TECHNICAL NOTES

### Face Detection on AR Glasses
- **Meta Quest 3**: Passthrough Camera API gives frame access; run MediaPipe Face Detection or TFLite model on frames
- **Magic Leap 2**: RGB camera accessible via Android Camera2 API; ML2-specific ML APIs
- **Android XR Glasses**: Standard Android Camera2 API; Google ML Kit Face Detection
- **Meta "Name Tag"**: Meta is developing built-in facial recognition for Ray-Ban Meta glasses (announced Feb 2026); not available as developer API yet
- **XREAL Air 2 Ultra**: No RGB camera - cannot do face detection from glasses; would need phone camera

### GPS World Anchoring Implementation
- **ARCore Geospatial API** (Android XR / Galaxy XR): Best option. Uses VPS + GPS for sub-meter accuracy at locations with Street View coverage
- **Custom GPS anchoring** (all other platforms): Use phone GPS + compass + IMU for approximate placement; accuracy ~3-10 meters; no VPS correction
- **Hybrid approach**: Use GPS for rough placement, then visual landmarks (SLAM) for refinement

### Mesh Networking Options
- **Android WiFi Direct** (WifiP2pManager): Available on Android-based devices; P2P connections without router
- **Bluetooth LE Mesh**: Android BLE APIs; lower bandwidth but lower power
- **WebRTC**: Works on any device with browser; good for WebXR apps
- **Multipeer frameworks**: Custom UDP broadcast on local WiFi for device discovery + TCP for data

---

## SOURCES

- [How To Install APK File To Meta Quest](https://shiifttraining.com/how-to-install-apk-file-to-a-meta-quest-headset/)
- [XREAL SDK Documentation](https://docs.xreal.com/)
- [XREAL Developer Portal](https://developer.xreal.com/)
- [XREAL Air 2 Ultra Announcement](https://www.prnewswire.com/news-releases/xreal-jump-starts-the-future-of-affordable-full-featured-spatial-computing-announces-xreal-air-2-ultra-ar-glasses-302027809.html)
- [RayNeo Developer Portal](https://www.rayneo.com/pages/developer)
- [RayNeo Open Platform](https://open.rayneo.com/)
- [Vuzix Developer Resources](https://support.vuzix.com/docs/developer-resources)
- [Vuzix Ultralite SDK (GitHub)](https://github.com/Vuzix/ultralite-sdk-android)
- [Vuzix Z100 Smart Glasses](https://www.vuzix.com/products/z100-smart-glasses)
- [Magic Leap 2 Developer Documentation](https://developer-docs.magicleap.cloud/docs/guides/ml2-overview/)
- [Magic Leap 2 Enabling Developer Mode](https://www.magicleap.care/hc/en-us/articles/10964519849357-Enabling-Developer-Mode)
- [Magic Leap 2 Sensors Documentation](https://developer-docs.magicleap.cloud/docs/device/hardware/sensors/)
- [Magic Leap 2 Hardware Specs](https://developer-docs.magicleap.cloud/docs/device/hardware/hardware-specs/)
- [Rokid AR SDK Platform](https://ar.rokid.com/sdk?lang=en)
- [Rokid Glasses Product Page](https://global.rokid.com/products/rokid-glasses)
- [RayNeo X2 Product Page](https://www.rayneo.com/products/tcl-rayneo-x2)
- [RayNeo X3 Pro Product Page](https://www.rayneo.com/products/x3-pro-ai-display-glasses)
- [Snapdragon Spaces SDK](https://spaces.qualcomm.com/sdk)
- [Snapdragon Spaces AR SDK](https://spaces.qualcomm.com/developer/ar-sdk/)
- [Meta OpenXR SDK (GitHub)](https://github.com/meta-quest/Meta-OpenXR-SDK)
- [Meta Passthrough Camera API](https://developers.meta.com/horizon/documentation/spatial-sdk/spatial-sdk-pca-overview/)
- [Unity AR Foundation for Meta Quest](https://blog.learnxr.io/xr-development/ar-foundation-with-meta-openxr-package)
- [Samsung Galaxy XR Sideloading](https://www.uploadvr.com/samsung-galaxy-xr-has-easy-sideloading-and-open-bootloader/)
- [Samsung Galaxy XR Specs](https://vr-compare.com/headset/samsunggalaxyxr)
- [Android XR SDK Developer Preview 3](https://android-developers.googleblog.com/2025/12/build-for-ai-glasses-with-android-xr.html)
- [ARCore Geospatial API](https://developers.google.com/ar/develop/geospatial)
- [ARCore for Jetpack XR](https://developer.android.com/develop/xr/jetpack-xr-sdk/arcore)
- [Android XR for WebXR](https://developer.android.com/develop/xr/web)
- [WebXR Browser Support (2026)](https://threejsresources.com/vr/blog/best-vr-headsets-with-webxr-support-for-three-js-developers-2026)
- [Snap Spectacles Developer Build](https://www.spectacles.com/build)
- [Snap Specs 2026 Announcement](https://newsroom.snap.com/launch-specs-2026)
- [Snap OS 2.0 with WebXR](https://techcrunch.com/2025/09/15/snap-unveils-snap-os-2-0-with-native-browser-webxr-support-and-more/)
- [Meta "Name Tag" Facial Recognition](https://www.macrumors.com/2026/02/13/meta-facial-recognition-smart-glasses/)
- [Even Realities G1](https://www.evenrealities.com/g1)
- [Even Hub Developer Portal](https://evenhub.evenrealities.com/)
- [Google Android XR Glasses 2026](https://9to5google.com/2025/12/08/android-xr-glasses-displays-2026/)
- [XREAL Project Aura Android XR](https://www.uploadvr.com/xreals-project-aura-android-xr-tethered-compute/)
- [Xreal Air 2 Ultra Specs (VRcompare)](https://vr-compare.com/headset/xrealair2ultra)
- [Meta Quest 3 Specs (VRcompare)](https://vr-compare.com/headset/metaquest3)
- [Rokid AR Lite Specs](https://www.xrtoday.com/augmented-reality/rokid-ar-lite-rokids-latest-spatial-computing-specs/)
- [Unity AR Development Manual](https://docs.unity3d.com/6000.3/Documentation/Manual/AROverview.html)
- [Vuzix Shield Product Page](https://www.vuzix.com/products/vuzix-shield-smart-glasses)
