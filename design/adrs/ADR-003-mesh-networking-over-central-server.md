# ADR-003: Mesh Networking Over Central Server

**Status:** Accepted
**Date:** 2026-03-08
**Decision Makers:** Project team

## Context

D-Space needs a communication layer for operatives to discover each other, exchange identity data, relay chat messages, broadcast reputation updates, and share spatial anchor data. The Daemon novels explicitly use a distributed mesh network with NO central authority.

## Decision

Implement a **peer-to-peer mesh network** using:

1. **UDP broadcast** for local network peer discovery (port 7734)
2. **UDP unicast** for direct peer-to-peer messaging (port 7733)
3. **Multi-hop relay** with configurable hop limit (default 5) for reaching non-adjacent peers
4. **Message deduplication** via message ID tracking to prevent loops
5. **RSA/AES hybrid encryption** for end-to-end message security
6. **Heartbeat protocol** (5s interval) for peer liveness detection
7. Future: WiFi Direct and BLE for wider device discovery

## Consequences

### Positive
- No central server = no single point of failure (faithful to the Daemon)
- Works on local networks without internet
- Privacy-preserving — no server sees traffic
- Scales naturally as more operatives join

### Negative
- Message delivery is best-effort, not guaranteed
- Discovery range limited to local network (until WiFi Direct / BLE is added)
- Higher battery usage from constant broadcasting
- No offline message queuing (messages lost if recipient is offline)

### Risks
- UDP broadcasts may be blocked on some enterprise networks
- Mesh routing at scale (100+ nodes) needs optimization
- NAT traversal needed for cross-network communication

## Alternatives Considered

1. **Firebase/WebSocket central server**: Reliable delivery but creates central dependency
2. **libp2p**: Mature P2P library but heavy dependency and complex setup
3. **IPFS**: Content-addressed storage is overkill for real-time messaging

## Related

- `MeshNetworkManager.cs` — Core mesh implementation
- `PeerDiscovery.cs` — UDP broadcast discovery
- `DarknetProtocol.cs` — Encryption layer
- Daemon novel reference: The darknet uses UWB + WiMax mesh
