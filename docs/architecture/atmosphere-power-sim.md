# Atmosphere / power sim

Full detail: `docs/project-plan.md` §5 "Architectural notes", Appendix A6-A7, §7 Phase 0 Spike 2.

## Settled

- **Atmosphere = lumped per-cell scalars with neighbor-to-neighbor diffusion, never per-tile CFD.** Each cell holds its own pressure/O₂ fraction/temperature; a breach vents only the directly-exposed cell toward vacuum, and diffusion (each cell moving toward the average of its own unsealed neighbors, one hop per tick) carries that effect outward — this is what makes distance/graph-hop-count matter (a cell several hops from a breach keeps some air for a moment) without simulating velocity, momentum, or advection. This ceiling is still deliberate — do not let it "improve" into real fluid dynamics.
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

## Before editing this subsystem

This is sim-vs-presentation-split code: no Godot node dependencies, headless-testable. Changes here are "keep on a short leash" per root `CLAUDE.md` — plan mode + passing tests required.
