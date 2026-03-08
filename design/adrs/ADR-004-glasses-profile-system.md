# ADR-004: Glasses Profile System for Device Compatibility

**Status:** Accepted
**Date:** 2026-03-08
**Decision Makers:** Project team

## Context

D-Space must run on 8+ different AR glasses and phones, each with different capabilities (GPS, cameras, hand tracking, FOV, display type). The app needs to gracefully adapt to each device's limitations.

## Decision

Implement a **GlassesProfileManager** that:

1. Auto-detects the device at startup via `SystemInfo.deviceModel`
2. Loads a hardware profile describing capabilities (GPS, camera, depth, tracking, etc.)
3. Subsystems query the active profile to enable/disable features
4. Fallback to "phone AR" profile if device is unrecognized
5. Profiles include rendering hints (scale, FPS target, FOV)
6. Companion phone app fills capability gaps (GPS relay for Quest, camera relay for XREAL)

## Consequences

### Positive
- Single codebase for all devices — no per-device builds needed
- Graceful degradation on limited hardware
- Easy to add new devices by adding a profile
- Users can manually select a profile if auto-detection fails

### Negative
- Cannot leverage device-specific features not exposed via Unity/OpenXR
- Auto-detection heuristics may fail for new/unknown devices
- Companion app adds complexity for glasses without GPS

### Risks
- New devices may have unique capabilities that don't fit the profile model

## Related

- `GlassesProfileManager.cs` — Profile system
- `docs/HARDWARE_GUIDE.md` — Supported devices
- `docs/SIDELOADING.md` — Installation per device
