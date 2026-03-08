# Sideloading D-Space on AR Glasses

## Overview

D-Space (Daemon Vision) can be sideloaded onto multiple AR glasses platforms. This guide covers the process for each supported device.

---

## Meta Quest 3 / 3S

**Best standalone option — largest developer community, mature SDK.**

### Prerequisites
- Meta Quest Developer Hub (MQDH) or SideQuest installed on PC
- USB-C cable
- Meta developer account

### Steps
1. **Enable Developer Mode**:
   - Install Meta Horizon app on phone → Settings → Developer → Enable Developer Mode
   - Or register at [developer.oculus.com](https://developer.oculus.com) and enable via headset settings

2. **Connect & Authorize**:
   - Connect Quest to PC via USB-C
   - Put on headset → Approve "Allow USB Debugging" prompt

3. **Install APK**:
   ```bash
   # Using ADB directly:
   adb install DSpace_Quest.apk

   # Or drag-and-drop into SideQuest/MQDH
   ```

4. **Launch**: The app appears in Apps → Unknown Sources in the Quest library

### Notes
- Quest has no GPS — pair with companion phone app for location
- Use passthrough mode for AR overlay
- Hand tracking available natively

---

## Samsung Galaxy XR

**Best D-Space experience — Android XR with full ARCore Geospatial API.**

### Prerequisites
- USB-C cable and ADB
- Samsung XR developer account

### Steps
1. **Enable Developer Mode**:
   - Settings → About → Tap Build Number 7 times
   - Settings → Developer Options → Enable USB Debugging

2. **Install**:
   ```bash
   adb install DSpace_AndroidXR.apk
   ```

3. **Permissions**: Grant camera, location, microphone, and nearby devices permissions when prompted.

### Notes
- Native GPS + ARCore Geospatial API = best GPS anchoring
- No developer mode workaround needed for sideloading (Android XR allows it natively)

---

## XREAL Air 2 Ultra

**Tethered optical see-through — requires Samsung phone as compute unit.**

### Prerequisites
- Compatible Samsung phone (S23 Ultra, S24 series, Z Fold)
- XREAL Nebula app installed
- ADB on PC

### Steps
1. **Enable Developer Mode on phone**:
   - Settings → Developer Options → USB Debugging ON

2. **Install on phone**:
   ```bash
   adb install DSpace_PhoneAR.apk
   ```

3. **Connect glasses**: Plug XREAL Air 2 Ultra into phone via USB-C

4. **Launch**: Open D-Space app → It auto-detects XREAL glasses and renders to them

### Notes
- Phone provides GPS, camera, and compute
- 52° FOV limits visible D-Space area
- No onboard camera — face detection uses phone rear camera

---

## RayNeo X2 / X3 Pro

**Standalone Android glasses with camera and GPS.**

### Prerequisites
- USB-C cable and ADB
- RayNeo developer firmware (contact RayNeo for unlock on newer firmware)

### Steps
1. **Enable Developer Mode**:
   - Settings → About Device → Tap Build Number 7 times
   - Settings → Developer Options → USB Debugging ON

2. **Install**:
   ```bash
   adb install DSpace_AndroidXR.apk
   ```

3. **Note**: Newer firmware may require permission unlock from RayNeo support.

### Notes
- Small 25-30° FOV — use HUD-only mode
- 16MP onboard camera for person detection
- Standalone GPS

---

## Rokid AR Lite

**Android compute puck with optical see-through glasses.**

### Prerequisites
- Rokid Station 2 compute puck
- USB-C cable and ADB

### Steps
1. **Enable Developer Mode** on Station 2:
   - Settings → About → Tap Build Number 7 times
   - Enable USB Debugging

2. **Install**:
   ```bash
   adb install DSpace_PhoneAR.apk
   ```

### Notes
- 90% Android APK compatibility
- 50° FOV — decent for D-Space
- No hand tracking — use touch controls on puck

---

## Vuzix Shield

**Enterprise binocular waveguide glasses.**

### Prerequisites
- Vuzix developer account
- ADB

### Steps
1. **Install via ADB**:
   ```bash
   adb install DSpace_AndroidXR.apk
   ```

2. **Enable Permissions**: Grant all requested permissions.

### Notes
- Stereo 13MP cameras — excellent for person detection
- Enterprise pricing and availability
- 28° FOV — HUD-only mode recommended

---

## Phone AR Fallback (Android/iOS)

**For development and testing without AR glasses.**

### Android
```bash
adb install DSpace_PhoneAR.apk
```

### iOS
Build Xcode project from Unity, then install via Xcode or TestFlight.

---

## Companion App

For glasses without GPS (Quest, XREAL), install the companion app on your phone:

```bash
adb install DaemonVision_Companion.apk
```

The companion app provides:
- GPS location relay via Bluetooth/WiFi
- Camera feed for person detection (if glasses lack cameras)
- Biometric authentication bridge
- Mesh network enhancement (extra radio for better peer discovery)

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "App not installed" | Check minimum Android version (API 26+). Clear storage and retry. |
| No AR tracking | Ensure camera permissions granted. Check lighting conditions. |
| No GPS anchors | Install companion app on phone. Check location permissions. |
| Mesh network not finding peers | Ensure both devices on same WiFi. Check firewall for port 7733-7734. |
| Low framerate | Reduce HUD opacity. Disable minimap. Lower detection interval. |
| Nameplates not appearing | Check person detection is enabled. Ensure camera feed is active. |
