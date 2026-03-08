# Hardware Guide — Building or Buying D-Space Glasses

## Recommended Devices by Use Case

### Best Overall D-Space Experience
**Samsung Galaxy XR** ($1,800)
- Full Android XR with ARCore Geospatial API
- GPS anchoring, hand tracking, eye tracking
- 109° FOV video passthrough
- Native sideloading without dev mode

### Best Budget Option
**Meta Quest 3S** (~$300) + Phone companion app
- Excellent developer ecosystem
- 96° FOV, hand tracking, passthrough
- Requires phone for GPS
- Largest community for AR/VR development

### Most Book-Accurate (Sunglasses Form Factor)
**RayNeo X2** (~$700) or **Rokid AR Lite** (~$500)
- Looks like regular sunglasses/glasses (closest to Daemon's HUD glasses)
- Standalone Android — sideload directly
- Smaller FOV but socially discreet

### Best Optical See-Through
**Magic Leap 2** ($3,300+)
- True optical transparency — see real world directly
- Best spatial accuracy and depth sensing
- Enterprise-grade but expensive

---

## DIY Smart Glasses Build

For makers who want to build custom D-Space glasses:

### Option A: Raspberry Pi + Waveguide Display
**Cost**: ~$200-400

**Components**:
- Raspberry Pi 5 (compute)
- Vufine+ or similar micro OLED display
- Pi Camera Module v3 (person detection)
- GPS module (u-blox NEO-M9N)
- IMU module (BNO055)
- Bluetooth 5.0 module
- Custom 3D-printed frame
- LiPo battery (3000mAh)

**Limitations**: No true AR overlay (display is in corner of vision), limited compute for ML.

### Option B: Android Phone + Birdbath Optics
**Cost**: ~$150-300

**Components**:
- Old Android phone (Pixel 4a+ or similar with ARCore support)
- Birdbath combiner optics (AliExpress/Banggood)
- 3D-printed frame to mount phone and optics
- USB-C GPS module (if phone GPS insufficient)

**This is the easiest DIY approach**: Use the phone's camera, GPS, and compute. The birdbath optics overlay the phone display onto your field of view.

### Option C: Snapdragon XR2 Development Kit
**Cost**: ~$500-800

**Components**:
- Qualcomm XR2 reference design or Thundercomm TurboX dev kit
- Waveguide or birdbath display module
- Stereo camera pair (for depth estimation)
- GPS + IMU
- Custom housing

**Best for serious development**: Full Snapdragon Spaces SDK support.

---

## Minimum Hardware Requirements for D-Space

| Feature | Required? | Used For |
|---------|-----------|----------|
| See-through display | Yes | AR overlay rendering |
| Camera (RGB) | Recommended | Person detection, object recognition |
| GPS | Recommended | Spatial anchoring (phone fallback OK) |
| WiFi | Yes | Mesh networking |
| Bluetooth | Recommended | Peer discovery, companion app |
| IMU (Accel+Gyro) | Yes | Head tracking, compass |
| Microphone | Recommended | Voice commands, voice chat |
| Speaker/Bone conduction | Recommended | Audio feedback, voice chat |
| Hand tracking | Optional | Gesture input (touch fallback) |
| Eye tracking | Optional | Gaze selection (head-gaze fallback) |
| Depth sensor | Optional | Better spatial mapping |

---

## Companion Phone Requirements

If your glasses lack GPS or cameras, pair with a phone:

**Android**: API 26+ (Android 8.0+), ARCore compatible
**iOS**: iPhone 8+ with ARKit support

The companion app handles:
- GPS location and heading → relayed to glasses via WiFi/BLE
- Camera feed → processed for person detection
- Biometric auth (fingerprint/face)
- Additional mesh networking radio
