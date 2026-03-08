# C4 Model — Level 2: Container Diagram

```mermaid
C4Container
    title Container Diagram — Daemon Vision

    Person(operative, "Operative", "Wears AR glasses")

    System_Boundary(dv, "D-Space Application") {
        Container(bootstrap, "Bootstrap & Core", "Unity/C#", "Lifecycle management, service locator, config")
        Container(identity, "Identity System", "Unity/C#", "Darknet addresses, callsigns, biometric auth")
        Container(spatial, "Spatial Engine", "Unity/C# + ARFoundation", "GPS anchoring, world mesh, coordinate conversion")
        Container(hud, "HUD Renderer", "Unity/C# + Shaders", "Nameplates, compass, minimap, status bar, quest paths")
        Container(social, "Social Systems", "Unity/C#", "Reputation, factions, classes, leveling (1-200)")
        Container(quest, "Quest Engine", "Unity/C#", "Quest management, objectives, rewards")
        Container(economy, "Economy", "Unity/C#", "Darknet credits, transactions, transfers")
        Container(network, "Mesh Network", "Unity/C# + UDP", "P2P discovery, message relay, encryption")
        Container(detection, "Detection", "Unity/C# + ML", "Person detection, threat assessment")
        Container(comms, "Communication", "Unity/C#", "Chat channels, spatial voice, DMs")
        Container(input, "Input System", "Unity/C#", "Gaze tracking, gestures, voice commands")
        Container(profiles, "Device Profiles", "Unity/C#", "Hardware capability detection and adaptation")
    }

    System_Ext(companion, "Companion Phone App", "Android/Java — GPS relay, biometric bridge")
    System_Ext(gps, "GPS")
    System_Ext(meshPeer, "Peer Operative's D-Space")

    Rel(operative, input, "Gaze, gesture, voice")
    Rel(input, hud, "Selection events")
    Rel(hud, operative, "Renders AR overlay")
    Rel(bootstrap, identity, "Initializes")
    Rel(bootstrap, spatial, "Initializes")
    Rel(identity, network, "Broadcasts identity")
    Rel(spatial, gps, "Queries position")
    Rel(network, meshPeer, "UDP mesh messages")
    Rel(detection, hud, "Person positions → nameplates")
    Rel(social, hud, "Reputation/faction data → display")
    Rel(quest, hud, "Quest paths → rendering")
    Rel(companion, spatial, "GPS relay")
    Rel(companion, detection, "Camera relay")
    Rel(profiles, hud, "Capability flags")
```

## Container Responsibilities

| Container | Key Classes | Role |
|-----------|------------|------|
| Bootstrap & Core | DSpaceManager, ServiceLocator, DarknetBootstrap | Lifecycle, DI, subsystem registration |
| Identity | DarknetIdentityManager, BiometricAuth | Who you are in D-Space |
| Spatial | SpatialAnchorManager, GPSLocationProvider | Where things are in the world |
| HUD | HUDManager, NameplateRenderer, CompassOverlay | What you see |
| Social | ReputationSystem, FactionManager, ClassSystem | Your standing and affiliations |
| Quest | QuestManager | What you're doing |
| Economy | DarknetEconomy | What you own |
| Network | MeshNetworkManager, PeerDiscovery, DarknetProtocol | How you communicate |
| Detection | PersonDetector, ThreatAssessment | Who is around you |
| Communication | ChatSystem, VoiceChannelManager | Real-time messaging |
| Input | GazeInput, GestureRecognizer, VoiceCommands | How you interact |
| Profiles | GlassesProfileManager | What your hardware can do |
