# Design Documentation

## Structure

- **diagrams/**: C4 Model architecture diagrams (Mermaid.js format)
  - `c4-context.md` — Level 1: System Context
  - `c4-container.md` — Level 2: Container
  - `c4-component.md` — Level 3: Component
- **adrs/**: Architecture Decision Records
  - `ADR-001-initial-architecture.md` — Foundational architecture decisions
  - `ADR-template.md` — Template for new ADRs

## C4 Model Levels

| Level | Purpose | Audience |
|-------|---------|----------|
| 1. Context | System in its environment | Everyone |
| 2. Container | High-level deployable units | Technical staff |
| 3. Component | Internal structure of containers | Developers |
| 4. Code | Class/module level (generated from source) | Developers |

## How to View Diagrams

Mermaid diagrams render natively in GitHub, VS Code (with extensions), and most modern markdown viewers. For standalone rendering, use [Mermaid Live Editor](https://mermaid.live).

## ADR Process

1. Copy `adrs/ADR-template.md` to `adrs/ADR-NNN-short-title.md`
2. Fill in context, decision, consequences, and alternatives
3. Set status to "Proposed"
4. After team review, update status to "Accepted"
