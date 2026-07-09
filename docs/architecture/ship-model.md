# Ship model

Full detail: `docs/project-plan.md` §5 "Ship representation" and Appendix A (the deepest structural decision in the project — everything touches it).

## Settled

- **Three placement tiers, not a voxel grid** (Appendix A1-A2):
  - **Tier 1 — Structural** (grid/edge-snapped): floors/ceilings per tile, walls per shared edge, machinery footprints. Drives pathfinding, airtightness, the flood-fill solver.
  - **Tier 2 — Fixtures** (surface-attached, free position): conduits, switches, lights, valves. Attach to a surface the structural tier exposes; never affect airtightness; feed the network graphs.
  - **Tier 3 — Loose objects**: items/cargo, pure physics transform, drift in zero-g.
- **1 m tiles.** Decks stacked for verticality (A5); deck-to-deck traversal via ladders/lifts (gravity) or hatches (zero-g), handled by the movement system.
- **Edges are first-class** (A3): an edge is the boundary between two adjacent tiles (or tile/outside). One wall per edge serves both tiles — never `WallA <> WallB` doubling. Same rule for floor/ceiling boundaries between decks.
- **Machinery** (A4): declares a footprint (1+ tiles) and optionally a mount surface. Floor-standing machinery blocks pathfinding; wall/floor-mounted devices are fixtures and don't.
- **Building is verb-driven** (A8): placement creates a placeholder a task fulfils over time (what time-acceleration skips). Reach is proximity-limited (~2 tiles) or via EVA.
- **Rendering is a pure function of model state** (A8) — this is what lets an off-screen ship simulate with no meshes loaded (see `multi-ship-fleet.md`).
- **Wall covers are post-MVP, optional** (A7): a real Tier-2 fixture over exposed wiring/piping, doesn't affect airtightness, gives protection/insulation/(later) habitability bonuses. MVP ships exposed-only.

## Open sub-decisions (A9) — don't block Phase 0/1 on these

- Conduit connectivity granularity: adjacent-cell (recommended for MVP) vs. explicit port-to-port.
- Data/signal networks: later, same solver when wanted.
- Deck height in world units: defer to greybox feel.

## Before editing this subsystem

Airtightness reads Tier 1 only; networks (power/data) read Tier 2 connectivity. Don't let a fixture affect airtightness or a structural change skip a network recompute.
