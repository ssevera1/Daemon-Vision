# C4 Model — Level 1: System Context Diagram

```mermaid
C4Context
    title System Context — Daemon Vision (D-Space)

    Person(operative, "Darknet Operative", "User wearing AR glasses running D-Space")
    Person(peerOp, "Peer Operative", "Other darknet operatives nearby")
    Person(civilian, "Civilian", "Non-darknet person detected by D-Space cameras")

    System(dspace, "D-Space", "AR overlay system that projects the darknet's virtual layer onto the real world via smart glasses")

    System_Ext(gps, "GPS Satellites", "Provides positioning for spatial anchoring")
    System_Ext(arcore, "ARCore / ARKit", "Platform AR services (plane detection, tracking)")
    System_Ext(meshNet, "Local Mesh Network", "WiFi/BLE peer-to-peer network between operatives")
    System_Ext(companion, "Companion Phone App", "GPS relay, camera feed, biometric auth for glasses without these capabilities")

    Rel(operative, dspace, "Interacts via gaze, gestures, voice")
    Rel(dspace, operative, "Renders HUD overlay, nameplates, quest paths")
    Rel(dspace, meshNet, "Discovers peers, exchanges data")
    Rel(peerOp, meshNet, "Broadcasts identity, receives messages")
    Rel(dspace, gps, "Queries position")
    Rel(dspace, arcore, "Queries planes, anchors, camera")
    Rel(companion, dspace, "Relays GPS, camera, biometrics")
    Rel(dspace, civilian, "Detects and overlays 'Unnamed' nameplate")
```

## Context Summary

D-Space is a self-contained AR application that:
- Runs on smart glasses or phone
- Overlays a persistent virtual layer (D-Space) on the real world
- Communicates with nearby operatives via mesh networking (no central server)
- Uses GPS for spatial anchoring of virtual objects
- Detects people via camera and overlays darknet identity nameplates
- Optionally pairs with a companion phone for GPS/camera relay
