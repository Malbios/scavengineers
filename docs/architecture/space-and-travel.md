# Space and travel

Full detail: `docs/project-plan.md` §5 "Space representation — two layers".

## Settled

- **Two-layer model**, chosen specifically to get "infinite, seamless" space without float-precision jitter:
  - **Strategic layer:** a map/graph of locations (stations, wreck fields, POIs) — pure data, no physics, arbitrarily large. Felt scale lives here.
  - **Tactical layer ("bubbles"):** bounded, origin-local physics scenes instantiated around wherever the player is — a transit bubble (player's ship at origin) or an encounter bubble (target + player's ship). Always re-centered near origin, always small (≤~10 km) so floats never jitter.
- This gives seamless-*feeling* travel with transitions — not literally-continuous cross-universe flight (deliberately avoids the Star Citizen streaming problem).
- **A bubble is the natural unit of "fully simulated."** Ships in the current bubble run full fidelity; everything else runs the coarse multi-ship LOD tick (see `multi-ship-fleet.md`). One concept, two payoffs.
- **MVP uses a simplified/abstracted travel step**, not full Newtonian ship piloting/docking — that's deferred (see plan §4 "Deferred to post-MVP phases").

## Implemented today (greybox stand-in — known drift from the model above)

`Scenes/World.tscn` holds **every** destination as a hand-placed sibling node group at a fixed
offset in one scene: the Home Ship, 2 Stations, and 5 Derelict instances (all instancing the
shared `Scenes/Derelict.tscn`, differentiated by `ShipSim.LayoutId` / `ProcedurallyGenerate`).
Travel runs through `TravelConsoleVerbTarget` (+ `TravelMapPanel`, `DockingMinigamePanel`):

- A destination is one unified int — `0..StationCount-1` = Station N, then Derelicts.
- Arriving calls `SetShipPresence`, which toggles `Node3D.Visible`, every descendant
  `CollisionShape3D.Disabled`, and every `IPhysicsPresenceAware` pickup's freeze state, so only
  the current destination is physically present.
- One shared Home-Ship-side airlock per kind (`DerelictAirlock` / `StationAirlock`) gets its far
  side repointed at the current destination via `AirlockDoorVerbTarget.RebindFarSide`; each
  Station additionally has its own destination-side door, and the two only bridge atmosphere when
  both report open.

**This is the retrofit the two-layer model exists to avoid, and it has already happened five
times.** The earlier version of this doc warned "don't extend the 'just add another fixed sibling
group' pattern to a second wreck" — that warning is now historical, not preventative. Two
consequences worth naming rather than rediscovering:

- `SetShipPresence` hides and decollides a group but does **not** stop its `_PhysicsProcess`, so
  every `ShipSim` in the scene — 13 of them, since each Derelict instance carries a second-deck
  `ShipSim` as well — ticks full-fidelity atmosphere/wear/fire every physics frame regardless of
  where the player is. That's exactly the seam `multi-ship-fleet.md`'s sim-LOD rule reserves — it
  is now a real need, not a speculative one.
- Every destination shares one world origin, so "felt distance" is entirely a travel-timer
  abstraction; nothing in the scene reflects the strategic layer at all.

The intended shape stays as described above: the strategic layer picks a destination and a bubble
is **instantiated at runtime**. Migrating to it means replacing the fixed sibling groups with
runtime instancing of `Derelict.tscn` under a bubble root — the per-instance data (`LayoutId`,
seed, breach placement) is already data-driven, so the ship *content* is not the blocker; the
scene topology and `TravelConsoleVerbTarget`'s parallel NodePath arrays are.

## Before editing this subsystem

Any physics-bearing scene must stay within the ≤~10 km bubble bound and re-center near origin — don't let a "just this once" scene span the strategic layer's distances directly.

Adding a **sixth** hand-placed destination group is the point to stop and do the runtime-instancing
work instead — each new one now costs a scene edit plus four parallel NodePath array entries on the
travel console, and adds another always-ticking `ShipSim`.
