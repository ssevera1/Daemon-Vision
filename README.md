# DAEMON VISION — D-Space AR Overlay

> *"The Daemon is watching. The Daemon is listening. The Daemon has awakened."*
> — Daniel Suarez, *Daemon*

**Daemon Vision** brings the augmented reality darknet from Daniel Suarez's *Daemon* and *Freedom(TM)* novels to life. It overlays **D-Space** — a persistent, GPS-anchored virtual layer — onto the real world through smart glasses, recreating the full darknet operative experience: floating identity nameplates, quest threads, reputation systems, mesh networking, threat detection, and a decentralized economy.

No central server. No corporate cloud. Just a peer-to-peer mesh network of operatives running D-Space on their AR glasses — exactly as Sobol designed it.

---

## What Is D-Space?

In the Daemon novels, **D-Space (Darknet Space)** is an augmented reality overlay built on the GPS grid. Darknet operatives wear HUD glasses that project virtual objects, information, and game-like mechanics onto the physical world. Every person, place, and thing can carry semantic tags visible only to authenticated operatives.

This project recreates that vision as a real, sideloadable application for today's AR glasses.

### Features Implemented

| Feature | Novel Reference | Implementation |
|---------|----------------|----------------|
| **Floating Nameplates** | Call-outs above every operative showing callsign, level, class, faction, reputation stars | World-space billboarded UI with distance fade, threat-level color coding |
| **Threat Indicators** | Red outlines around hostiles, visible through walls | Pulsing outline shader with behavioral threat scoring |
| **Quest Threads** | Glowing AR paths guiding operatives to objectives | LineRenderer with animated golden flow shader + waypoint markers |
| **200-Level System** | Sobol's MMORPG-inspired progression (Lv.1–200) | XP from quests, reputation, contributions; exponential scaling |
| **7 Darknet Classes** | Fighter, Sorcerer, Shaman, Scout, Fabricator, Journalist, Rogue | Full class definitions with level-gated abilities |
| **5 Factions** | Order of Merritt, Merittorious Raiders, Dark Rose, GamerZ, Independent | Faction management with reputation/level gates |
| **5-Star Reputation** | Crowd-sourced rating system — the currency of trust | Weighted running average with cooldowns and minimum level requirements |
| **Darknet Credits** | Reputation-backed economy | Credits, transfers with mesh-verified transactions, transaction history |
| **Mesh Networking** | Distributed P2P darknet with no central authority | UDP broadcast discovery + unicast messaging + multi-hop relay |
| **Encrypted Comms** | End-to-end encrypted darknet channels | RSA 2048 + AES hybrid encryption, signed messages |
| **Biometric Auth** | Biometrically-keyed HUD glasses (retinal/fingerprint) | Android BiometricPrompt integration |
| **GPS Anchoring** | Virtual objects anchored to the GPS grid | Equirectangular projection, persistent spatial anchors |
| **Chat & Voice** | Encrypted darknet communication channels | Public/faction/DM chat, spatial proximity voice |
| **Person Detection** | HUD identifies people in view | ML-based detection via Unity Sentis (ONNX models) |
| **Compass & Minimap** | Tactical awareness overlay | Cardinal compass strip + radar-style minimap with operative/anchor blips |

---

## Architecture

Built on a **subsystem architecture** inspired by the Daemon's modular design — 25+ independently-operable subsystems coordinated by a central manager.

```
DarknetBootstrap → registers subsystems → DSpaceManager → initializes in dependency order
                                              ↓
          ┌──────────────┬──────────────┬──────────────┬──────────────┐
       Identity      Spatial          HUD         Network        Detection
       (who you      (where things   (what you    (the darknet   (who's around
        are)          are)            see)         itself)        you)
          │              │              │              │              │
       Biometric    GPS Anchors    Nameplates    Mesh P2P       Person ML
       Callsigns    World Mesh     Threat Glow   Peer Discovery Threat Score
       Classes      Coordinates    Compass       Encryption     Depth Estimate
       Factions                    Minimap       Message Relay
       Reputation                  Quest Paths
       Levels                      Status Bars
```

### Design Principles

- **No central server** — P2P mesh networking, faithful to the novels (ADR-003)
- **Device-adaptive** — GlassesProfile system detects hardware and enables/disables features (ADR-004)
- **Subsystem isolation** — any subsystem can be disabled without affecting others (ADR-002)
- **Event-driven** — cross-system communication via C# events, loose coupling throughout

See [`design/adrs/`](design/adrs/) for Architecture Decision Records documenting the reasoning behind each major design choice.

---

## Project Structure

```
daemon-vision/
├── unity-project/                    # Main Unity AR application
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Core/                 # DSpaceManager, Bootstrap, ServiceLocator
│   │   │   ├── Identity/             # Darknet identities, biometric auth
│   │   │   ├── Spatial/              # GPS anchoring, world mesh, coordinates
│   │   │   ├── HUD/                  # Nameplates, threats, compass, minimap, quests
│   │   │   ├── UI/                   # Full UGUI implementation (10 panels)
│   │   │   ├── Social/               # Reputation, factions, classes, leveling
│   │   │   ├── Quest/                # Quest system with AR waypoints
│   │   │   ├── Economy/              # Darknet credits and transactions
│   │   │   ├── Network/              # P2P mesh, peer discovery, encryption
│   │   │   ├── Detection/            # ML person detection, threat assessment
│   │   │   ├── Communication/        # Chat channels, spatial voice
│   │   │   ├── Input/                # Gaze, gestures, voice commands
│   │   │   ├── Data/                 # Persistence, spatial DB, serialization
│   │   │   └── Config/               # DSpaceConfig, GlassesProfiles
│   │   ├── Shaders/                  # 5 custom shaders (holographic, nameplate, threat, quest, grid)
│   │   ├── Scenes/                   # DSpaceMain.unity, Calibration.unity
│   │   └── Editor/                   # Build configs, dev tools, subsystem inspector
│   ├── Packages/                     # Unity package manifest
│   └── ProjectSettings/              # Project, quality, tags, input settings
├── companion-app/                    # Android companion for glasses without GPS
│   └── app/src/main/
│       ├── java/.../companion/       # CompanionService, BiometricBridge, CompanionActivity
│       ├── res/                      # Layouts, styles, drawables
│       └── AndroidManifest.xml
├── design/                           # Architecture documentation
│   ├── diagrams/                     # C4 model diagrams (Mermaid.js)
│   ├── adrs/                         # Architecture Decision Records
│   └── daemon-novels-reference.md    # D-Space technical reference from the books
├── docs/                             # User-facing documentation
│   ├── SIDELOADING.md                # Install guide for each glasses platform
│   ├── HARDWARE_GUIDE.md             # Buy vs. build your D-Space glasses
│   └── BUILDING.md                   # Build from source guide
└── tools/build/                      # Build and deploy scripts
    ├── build.sh                      # Multi-target build automation
    └── deploy.sh                     # ADB deployment with auto-launch
```

**100 files | 54 C# scripts (15,400+ lines) | 5 shaders | 3 Java files | 2 Unity scenes**

---

## Supported Hardware

D-Space adapts to each device's capabilities via the GlassesProfile system:

| Device | Display | FOV | GPS | Camera | Hand Tracking | Best For |
|--------|---------|-----|-----|--------|---------------|----------|
| **Samsung Galaxy XR** | Video passthrough | 109° | Yes | Yes | Yes | Best overall D-Space experience |
| **Meta Quest 3 / 3S** | Video passthrough | 104° | Phone* | Yes | Yes | Best budget option, largest community |
| **Magic Leap 2** | Optical see-through | 70° | Phone* | Yes | Yes | True AR transparency |
| **Android XR Glasses (2026)** | Optical see-through | 70° | Yes | Yes | Yes | Future target, full ARCore Geospatial |
| **XREAL Air 2 Ultra** | Optical see-through | 52° | Phone* | No | No | Tethered, lightweight |
| **Rokid AR Lite** | Optical see-through | 50° | Phone* | Yes | No | Compact, Android compatible |
| **Vuzix Shield** | Optical see-through | 28° | Yes | Yes | No | Enterprise, great cameras |
| **RayNeo X2** | Optical see-through | 25° | Yes | Yes | No | Standalone sunglasses form factor |
| **Any ARCore/ARKit phone** | Phone screen | 60° | Yes | Yes | No | Development and testing fallback |

*\* Pair with the Companion Phone App for GPS relay*

---

## Quick Start

### Prerequisites

- **Unity 2022.3 LTS** or newer
- **Android Build Support** module (via Unity Hub)
- **Android SDK 28+** and **NDK r25+**
- **ADB** for device deployment

### Build & Deploy

```bash
# Clone
git clone https://github.com/ssevera1/Daemon-Vision.git
cd Daemon-Vision

# Open in Unity
# Unity Hub → Add Project → select unity-project/

# Build (command line)
./tools/build/build.sh quest        # Meta Quest 3
./tools/build/build.sh androidxr    # Android XR glasses
./tools/build/build.sh phone        # Phone AR
./tools/build/build.sh companion    # Companion phone app
./tools/build/build.sh all          # Everything

# Deploy to connected device
./tools/build/deploy.sh quest --launch
```

### Or Build from Unity Editor

1. Open `unity-project/` in Unity
2. Open `Assets/Scenes/DSpaceMain.unity`
3. Menu: **DaemonVision → Build → [Target Platform]**

### Sideloading

See the full sideloading guide for each device: **[docs/SIDELOADING.md](docs/SIDELOADING.md)**

---

## Development

### Editor Tools

| Menu Item | What It Does |
|-----------|-------------|
| DaemonVision → Tools → Spawn Test Operatives | Creates 5 simulated operatives with random identities |
| DaemonVision → Tools → Simulate GPS Location | Set simulated coordinates with presets |
| DaemonVision → Tools → Create D-Space Anchor | Place a GPS-anchored object at current view |
| DaemonVision → Tools → Generate Test Quest | Creates a quest with waypoints near current position |
| DaemonVision → Tools → Show Subsystem Status | Inspector window showing all subsystem states |
| DaemonVision → Tools → Reset Local Identity | Fresh start — clears identity data |

### Testing in Editor

D-Space runs in simulation mode in the Unity Editor:
- GPS is simulated via `DSpaceConfig.SimulatedLatitude/Longitude`
- Set `DSpaceConfig.SpawnTestOperatives = true` for simulated people
- Use Game + Scene view simultaneously for both perspectives

### Companion App

For glasses without onboard GPS (Quest, XREAL), build and install the companion phone app:

```bash
cd companion-app
./gradlew assembleDebug
adb install app/build/outputs/apk/debug/app-debug.apk
```

The companion app relays GPS coordinates, provides biometric authentication, and enhances mesh network discovery.

---

## The Darknet Classes

| Class | Color | Specialty | Example Abilities |
|-------|-------|-----------|-------------------|
| **Fighter** | Red | Combat, tactical awareness | Threat Scan, Shield Wall, AutoM8 Command, Razorback Link |
| **Sorcerer** | Purple | Hacking, tech, digital warfare | Network Probe, Darknet Curse, Invisibility Ring, System Override |
| **Shaman** | Green | Community building, healing | Community Pulse, Mediation Circle, Resource Map, Faction Diplomacy |
| **Scout** | Blue | Recon, intelligence, stealth | Extended Scan, Tracker, Low Profile, Drone Link, Ghost Mode |
| **Fabricator** | Orange | Building, engineering, making | Blueprint Overlay, Material Scanner, D-Space Architect, AutoM8 Builder |
| **Journalist** | Yellow | Information, media, verification | Record Mode, Broadcast, Fact Check, Public Quest, Archive Access |
| **Rogue** | Gray | Covert ops, deception | Spoof ID, Dead Drop, Shadow Step, Counter Intel, Phantom Network |

---

## Architecture Documentation

- **[C4 Context Diagram](design/diagrams/c4-context.md)** — System in its environment
- **[C4 Container Diagram](design/diagrams/c4-container.md)** — Internal containers and data flow
- **[C4 Component Diagram](design/diagrams/c4-component.md)** — HUD renderer internals + sequence diagrams
- **[ADR-001: Pipeline Architecture](design/adrs/ADR-001-initial-architecture.md)**
- **[ADR-002: Subsystem Pattern](design/adrs/ADR-002-subsystem-architecture.md)**
- **[ADR-003: Mesh Networking](design/adrs/ADR-003-mesh-networking-over-central-server.md)**
- **[ADR-004: Glasses Profiles](design/adrs/ADR-004-glasses-profile-system.md)**
- **[Daemon Novels Reference](design/daemon-novels-reference.md)** — Technical reference from the books

---

## Hardware Guide

Want to build your own D-Space glasses? See **[docs/HARDWARE_GUIDE.md](docs/HARDWARE_GUIDE.md)** for:
- Recommended devices by use case and budget
- 3 DIY build options (Raspberry Pi, phone + optics, Snapdragon XR2)
- Minimum hardware requirements
- Companion phone specifications

---

## Voice Commands

Activate with the wake word **"daemon"**, then speak a command:

| Command | Action |
|---------|--------|
| `daemon, scan` | Scan surroundings for operatives and anchors |
| `daemon, accept quest` | Accept the highlighted quest |
| `daemon, open map` | Toggle map overlay |
| `daemon, show quests` | Display quest log |
| `daemon, send message [text]` | Send chat message |
| `daemon, status` | Show full operative status |
| `daemon, identify` | Identify gazed target |
| `daemon, mark threat` | Flag target as hostile |
| `daemon, navigate to [place]` | Start navigation |
| `daemon, help` | List available commands |

---

## Roadmap

- [ ] WiFi Direct and BLE transport layers for wider mesh range
- [ ] Persistent cloud anchor sharing (optional, for cross-session D-Space objects)
- [ ] AutoM8 integration — interface with IoT devices and drones
- [ ] Razorback protocol — autonomous vehicle networking
- [ ] D-Space Architect mode — build persistent virtual structures
- [ ] Cross-platform voice codec (Opus) for voice channels
- [ ] Web dashboard for D-Space analytics and quest creation
- [ ] Community quest marketplace

---

## Credits

Inspired by the visionary work of **Daniel Suarez** in *Daemon* (2006) and *Freedom(TM)* (2010). This project is a fan-made recreation of the technology described in those novels — built to explore what a real-world D-Space might look like.

This is not affiliated with or endorsed by Daniel Suarez or his publishers.

---

## License

This project is open source. See [LICENSE](LICENSE) for details.

---

*"The darknet wasn't a destination. It was a way of seeing the world."*
