# ADR-002: Subsystem Architecture Pattern

**Status:** Accepted
**Date:** 2026-03-08
**Decision Makers:** Project team

## Context

D-Space requires 15+ independent subsystems (identity, spatial, HUD, networking, etc.) that need to initialize in dependency order, communicate with each other, and run across AR devices with vastly different capabilities (some lack GPS, cameras, or hand tracking).

## Decision

Adopted a **subsystem registration pattern** where:

1. All subsystems implement `IDSpaceSubsystem` with lifecycle methods (`Initialize`, `Tick`, `Shutdown`)
2. `DarknetBootstrap` registers subsystems with `DSpaceManager` in dependency order
3. `DSpaceManager` orchestrates initialization and per-frame updates
4. Subsystems discover each other via `GetSubsystem<T>()` after all are ready
5. Cross-subsystem communication uses C# events (observer pattern)
6. Each subsystem extends `SubsystemBase` for common lifecycle management

## Consequences

### Positive
- Any subsystem can be disabled without affecting others (critical for device compatibility)
- Clear dependency order prevents initialization races
- Event-based communication keeps subsystems loosely coupled
- Easy to add new subsystems without modifying existing code

### Negative
- Indirect communication can make debugging harder
- Event chains can create non-obvious execution paths
- Slight performance overhead from the abstraction layer

### Risks
- Event listener leaks if subsystems don't clean up properly (mitigated by `Shutdown()`)

## Alternatives Considered

1. **Direct references between systems**: Simpler but creates tight coupling
2. **Full ECS (Entity Component System)**: More performant but overkill for system-level management
3. **Message bus / mediator**: More decoupled but harder to debug and type-unsafe

## Related

- `DSpaceManager.cs` — Central coordinator
- `SubsystemBase.cs` — Base class
- `DarknetBootstrap.cs` — Registration order
- ADR-001 — Pipeline architecture
