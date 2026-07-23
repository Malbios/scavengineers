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

## Implemented today

- **Tier 1** is `Scavengineers.Sim.ShipModel.Deck` — cells, sealed edges, per-cell breaches
  (reason-tagged by `StructuralSurface`), per-*edge* wall breaches, and per-cell/per-edge health.
  The edge rule holds: `Deck.Normalize` canonicalizes every edge pair, so one wall serves both
  tiles and `WearSystem` decays each interior edge exactly once.
- **Tier 2** is `Fixture` and its subtypes (`Conduit`/`Switch`/`Machine`/`Battery`/`Thruster`/
  `Storage`), each carrying a tile + `FixtureSurface`. Conduit connectivity is **adjacent-cell**,
  i.e. the MVP option recommended below — `PowerSystem.AreConnected` is Manhattan distance ≤ 1.
- **Tier 3** is `PickupItem`/`ContainerPickupItem` — real `RigidBody3D`s that float in zero-g via
  `ShipAtmosphereZone`'s gravity override.
- **Multi-deck is real** (A5): a layout may declare a `secondDeck` plus a `ladderCell`
  (`Data/Ships/layouts.json`); the second deck is its own `ShipSim` with `DeckIndex = 1` and a
  `PrimaryDeckRef`, offset vertically by `ShipBuildTarget.DeckYOffset` so deck 2's floor plane
  meets deck 1's ceiling plane. Depth is capped at one extra deck. Traversal is
  `LadderVerbTarget` + `Player.BeginClimbing`.
- **Layouts are data-driven**: `ShipLayoutCatalog` reads `Data/Ships/layouts.json`; a ship can
  instead set `ProcedurallyGenerate` and roll a `ShipLayoutGenerator` layout from a saved seed.
- **Building is verb-driven and free-form** via `ShipBuildTarget` — floor/ceiling panels, wall
  segments on edges, floor and wall conduits, machines on edges, plus `Extend Floor` to claim new
  cells beyond the ship's original footprint.

Not yet built: wall covers (post-MVP by design), machinery footprints larger than one tile, and
pathfinding of any kind (no NPCs yet, so nothing reads the structural tier for navigation).

## Open sub-decisions (A9) — don't block Phase 0/1 on these

- Conduit connectivity granularity: adjacent-cell (recommended for MVP) vs. explicit port-to-port.
- Data/signal networks: later, same solver when wanted.
- Deck height in world units: defer to greybox feel.

## Before editing this subsystem

Airtightness reads Tier 1 only; networks (power/data) read Tier 2 connectivity. Don't let a fixture affect airtightness or a structural change skip a network recompute.
