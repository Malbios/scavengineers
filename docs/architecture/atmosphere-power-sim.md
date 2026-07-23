# Atmosphere / power sim

Full detail: `docs/project-plan.md` §5 "Architectural notes", Appendix A6-A7, §7 Phase 0 Spike 2.

## Settled

- **Atmosphere = lumped per-cell scalars, never per-tile CFD.** Each cell holds its own pressure/O₂ fraction/temperature. A breach (or an open airlock bridging to a room that has one) vents its *entire* connected-to-outside component uniformly, toward vacuum, at the same rate for every cell — matching real depressurization, where internal pressure equalizes at the speed of sound, vastly faster than air escapes through a hole, so a connected volume never develops a distance-based gradient in practice. Neighbor-to-neighbor diffusion still exists, but only for sealed, non-vented components (e.g. two rooms mixing through an open door with no external vacuum driving force) — it never runs for a vented component. This ceiling is still deliberate — do not let it "improve" into real fluid dynamics (no velocity/momentum/advection).
- **The one-solver trick (A6):** atmosphere, power, data/signal (later), and room-detection all reduce to the *same* connectivity engine reading the ship's structural tier:

  | System | "Network" is… | Flow blocked by… |
  |---|---|---|
  | Atmosphere | flood-fill of connected cells | sealed edges/floors/ceilings, closed doors |
  | Power | connected component of conduits + attached machines | a gap in the cable run, or an open switch |
  | Data/signal (later) | connected component of data conduits | same |
  | Rooms | flood-fill of enclosed cells + required fixtures | sealed boundaries |

  Write **one** graph/flood-fill solver and reuse it for all four — don't fork a second solver for a new subsystem.
- **Recompute lazily on structural change** (wall placed/removed, switch toggled, breach opened) — not per frame.
- **Conduit fire loop (A7):** conduits are Tier-2 fixtures with a type/quality and condition. Powered + damaged + O2 present → spark/fire; high O2 raises risk. This must fall out of (power state) × (condition) × (local atmosphere) — never scripted per-incident.
- **Phase 0 Spike 2** is this subsystem's first implementation: minimal atmosphere + power in pure headless C# with unit tests (a breach vents O2; an open switch cuts power to a region). Load-bearing for everything else — get this right before building on top of it.

## Implemented today

All of this lives in `Scavengineers.Sim` (pure C#, no Godot references) and is covered by
`Scavengineers.Sim.Tests`:

| Type | Role |
|---|---|
| `Connectivity/ConnectivitySolver` | the one flood-fill; subsystems supply blocking rules via `IConnectivityGraph<TNode>.Neighbors` |
| `ShipModel/Deck` | the shared structural tier — cells, sealed edges, per-cell and per-edge breaches, fires, fixtures, floor/ceiling/wall health |
| `Atmosphere/AtmosphereSystem` | per-cell scalars; per component, exactly one of Vent (connected to Outside, or externally marked) *or* Diffuse+Regenerate |
| `Atmosphere/AirlockBridge` | bolt-on link between two independent ships' systems; averages when sealed, marks both externally-vented when either side leaks |
| `Power/PowerSystem` | conductive fixtures joined by adjacent-cell connectivity; an open switch conducts nowhere |
| `Hazards/FireSystem` | the A7 loop — powered × damaged × O2 → ignite; heat-damages neighboring conduits, consumes O2, extinguishes below an O2 floor |
| `Hazards/WearSystem` | passive decay of every fixture Condition and every structural surface's health (~3 h full-to-zero); Battery/Thruster excluded, since their Condition means charge |

`Scripts/Ship/ShipSim.cs` is the Godot-side owner: it builds the `Deck` from exported grid
fields (or a `ShipLayoutCatalog` entry / `ShipLayoutGenerator` roll), seeds fixtures and initial
breaches, and ticks atmosphere/fire/wear from `_PhysicsProcess`. It is also where the ship's
power *budget* lives (`BatteryCapacity` / `DemandedPower` / ship-wide brownout) — deliberately
above the Sim layer, since it's a game-balance rule rather than a connectivity one.

## Known deviations from the rules above (documented, not endorsed)

- **Rooms do not come from the solver.** The table above lists room-detection as the fourth
  consumer of the shared flood-fill. In practice the player's "which room am I in" resolution runs
  through `Scripts/Ship/ShipAtmosphereZone` — `Area3D` boxes generated from the *layout* by
  `ShipBuildTarget.GenerateAtmosphereZonesFromRoomLayout`, queried per frame with
  `PhysicsPointQueryParameters3D` and tie-broken by normalized containment margin. Some
  continuous-position → tile mapping is unavoidable for a physics player, but the *zones
  themselves* are a second, layout-derived notion of "room" that the solver's components don't
  feed. If room-detection ever becomes gameplay-load-bearing (crew pathing, per-room systems),
  derive the zones from `AtmosphereSystem.ComponentContaining` rather than growing this parallel
  path.
- ~~**Every power query re-runs the whole flood-fill.**~~ **Fixed.** `PowerSystem` used to be
  O(F³) end to end — `Neighbors` linear-scanned every fixture (making one `FindComponents` O(F²)),
  `PoweredNodes()` re-ran it per query, and `ShipSim.DemandedPower` called it *per fixture* while
  `IsPowered` called `DemandedPower` for its overload check. Five node types call
  `ShipSim.IsPowered` every physics frame and `FireSystem.Tick` calls it once per conduit, over a
  fixture count the player can grow without bound by placing conduits. Now: a
  `Dictionary<CellCoord, List<Fixture>>` tile index makes `Neighbors` O(1), `PoweredNodes()` is
  memoized, and `DemandedPower` resolves it once and tests set membership. O(F) overall.

  **The cache validates itself rather than being invalidated.** It holds a snapshot of the
  conductivity-relevant state (fixture id, tile, switch open, thruster charged) and compares field by
  field. A `Invalidate()` call chain was rejected deliberately: `TravelConsoleVerbTarget`'s drain loop
  mutates thruster `Condition` straight on `Deck.Fixtures` with no `PowerSystem` reference, so a
  forget-to-invalidate bug was a real risk, and a stale power grid fails as wrong behaviour rather
  than as an error. A self-computed signature cannot go stale. It deliberately excludes general
  `Condition`, which `WearSystem` decays every tick and which would defeat the cache entirely.

- **`AtmosphereSystem.IsConnectedToOutside` still has the un-memoized shape**, and is called every
  frame from `Player._PhysicsProcess`. Same fix applies if it ever matters.

## Before editing this subsystem

This is sim-vs-presentation-split code: no Godot node dependencies, headless-testable. Changes here are "keep on a short leash" per root `CLAUDE.md` — plan mode + passing tests required.
