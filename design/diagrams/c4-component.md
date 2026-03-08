# C4 Model — Level 3: Component Diagram (HUD Renderer)

```mermaid
C4Component
    title Component Diagram — HUD Renderer

    Container_Boundary(hud, "HUD Renderer") {
        Component(hudMgr, "HUDManager", "MonoBehaviour", "Canvas management, layout zones, opacity control, color scheme")
        Component(nameplates, "NameplateRenderer", "MonoBehaviour", "Floating call-outs above people: callsign, level, class, faction, stars")
        Component(threat, "ThreatIndicatorRenderer", "MonoBehaviour", "Red pulsing outlines on hostiles, off-screen directional arrows")
        Component(compass, "CompassOverlay", "MonoBehaviour", "Heading display with markers for quests, operatives, POIs")
        Component(statusBar, "StatusBarRenderer", "MonoBehaviour", "Top: callsign/level/credits. Bottom: mesh status/peer count")
        Component(questHUD, "QuestHUDRenderer", "MonoBehaviour", "Glowing quest thread paths, waypoint markers, objective tracking")
        Component(minimap, "MinimapRenderer", "MonoBehaviour", "Radar-style minimap with operative/anchor blips")
    }

    Component_Ext(personDet, "PersonDetector", "Detection subsystem")
    Component_Ext(threatSys, "ThreatAssessment", "Detection subsystem")
    Component_Ext(identMgr, "DarknetIdentityManager", "Identity subsystem")
    Component_Ext(questMgr, "QuestManager", "Quest subsystem")
    Component_Ext(anchorMgr, "SpatialAnchorManager", "Spatial subsystem")

    Rel(personDet, nameplates, "OnPersonDetected/Updated/Lost")
    Rel(identMgr, nameplates, "Identity data for matched operatives")
    Rel(threatSys, threat, "OnThreatDetected/Updated/Cleared")
    Rel(questMgr, questHUD, "OnQuestAccepted/Completed, objective updates")
    Rel(identMgr, statusBar, "Local identity data")
    Rel(anchorMgr, minimap, "Nearby anchor positions")
    Rel(identMgr, minimap, "Peer operative positions")
    Rel(hudMgr, nameplates, "Color scheme, opacity")
    Rel(hudMgr, threat, "Color scheme")
    Rel(hudMgr, compass, "Layout position")
```

## Data Flow: Person Detection → Nameplate

```mermaid
sequenceDiagram
    participant Camera as AR Camera
    participant PD as PersonDetector
    participant ID as IdentityManager
    participant NP as NameplateRenderer
    participant Mesh as MeshNetwork

    Camera->>PD: Camera frame (5 FPS)
    PD->>PD: ML person detection
    PD->>NP: OnPersonDetected(worldPos)
    NP->>ID: TryMatchIdentity(position)
    ID->>Mesh: Check peer broadcasts
    Mesh-->>ID: Known operative data
    ID-->>NP: DarknetIdentity or null
    NP->>NP: Create/update nameplate
    Note over NP: Callsign, Lv.X, Class<br/>★★★☆☆ (count)<br/>Faction Name
    NP->>Camera: Billboard rendering (faces camera)
```

## Shader Pipeline

| Shader | Used By | Visual Effect |
|--------|---------|--------------|
| HolographicOverlay | General D-Space panels | Translucent glow + scan lines |
| NameplateShader | Nameplates | Rounded rect + border glow |
| ThreatOutline | Hostile indicators | Red pulsing outline (visible through walls) |
| QuestPath | Quest threads | Flowing golden particles along path |
| DSpaceGrid | Scan mode | Subtle GPS coordinate grid overlay |
