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
group' pattern to a second wreck" — that warning is now historical, not preventative.

### The strategic layer is real data now

`Data/destinations.json` + `DestinationCatalog` hold the location list — id, kind, display name,
map position — which is the "map/graph of locations, pure data, no physics" half of the model. It
replaced two parallel inspector arrays (`StationMapPositions`/`DerelictMapPositions`) *and* labels
built by index (`$"OBJECT_DERELICT_{i + 1}"`), so a destination's identity now lives in one row of
one file. `TravelConsoleVerbTarget` warns at startup if the catalog and the scene wiring disagree
about how many destinations exist.

**Destination ordering in that file is load-bearing**: a destination is addressed by its index
across the whole list, stations first. Appending is safe; reordering or removing silently repoints
in-flight `CargoDelivery` contracts. `DestinationCatalogTests` guards the ordering and id
uniqueness against the real file.

### The tactical layer is not — this is the remaining gap

Ships are still hand-placed sibling groups, not instantiated into a bubble at runtime. What that
costs today, now that the other consequences are fixed:

- ~~All 13 `ShipSim`s tick full-fidelity every frame~~ — **fixed**: absent ships drop to a coarse
  LOD tick (see `multi-ship-fleet.md`).
- Every destination shares one world origin, so "felt distance" is entirely a travel-timer
  abstraction; nothing in the scene reflects the strategic layer.
- Every destination's geometry is resident whether or not you're there.

**Both destination kinds are now real scenes.** Derelicts always were (`Scenes/Derelict.tscn`);
`Scenes/Station.tscn` was extracted out of `World.tscn`, which both Stations now instance —
Station 1 with only a transform, Station 2 with its own save ids and its own two NPCs.

Verifying that extraction needed a tool, because **nothing in the test suite loads `World.tscn`**:
`Scavengineers.NodeTests` is a separate, scene-less Godot project, and `WorldSceneRegressionTests`
reads the file as *text*. `godot --headless --quit` catches an unresolved resource but not a mesh
silently pointing at the wrong `SubResource` of the right type — the "locally plausible but globally
wrong" failure `docs/project-plan.md` §6 warns about. `Tools/scene_digest.gd` closes that gap:

```powershell
& $godot --headless --path . --script res://Tools/scene_digest.gd -- res://Scenes/World.tscn
```

It instantiates a scene *without* adding it to the tree (so no `_Ready` runs) and prints every
node's path, class, script, groups and stored properties, expanding sub-resources inline so a mesh
is compared by its actual size and material rather than by which id it points at. Digest before a
scene refactor, digest after, diff. The Station extraction was verified this way: over all 31,277
digest lines the only change was the 16 node headers renamed by unifying `ShopFigure2`/
`ContractFigure2` onto the shared scene's names. **Run it before and after any `.tscn` surgery.**

One hazard instancing adds: a SaveId authored in a shared scene is inherited by every instance that
doesn't override it, and `SaveManager` keys purely by id — so two live nodes sharing one id means
the second capture silently overwrites the first and every instance loads the same state back.
`WorldSceneRegressionTests` resolves each instance's *effective* id and asserts no two collide. It
found this already live in `Derelict.tscn`: `Deck2/Floor2` shipped one hardcoded id that no derelict
overrode, so all five wrecks' second-deck build state was one shared slot.

### The tactical layer is built from data now

`DestinationManager` instantiates every `DestinationCatalog` entry under `World.tscn`'s `BubbleRoot`
at startup and registers it with the travel console. That removed eight hand-placed sibling groups
from `World.tscn` (1108 → 474 lines) and all seven parallel `NodePath` arrays from
`TravelConsoleVerbTarget`. **Adding a destination is one row of `destinations.json`.**

A destination row carries `scene`, a position, and `overrides` — a node path → property → value map,
the data equivalent of a `.tscn` instance override block, which is exactly what it replaced. All
five Derelicts share `Derelict.tscn` and differ only by overrides. When a difference *isn't* a
scalar — Station 2's own figure placement, materials and idle timings — it goes in a variant scene
instead (`Station2.tscn` inherits `Station.tscn`), and the row just names it.

Overrides are applied before the instance enters the tree, because `ShipSim` reads `LayoutId` and
`ShipBuildTarget` reads `GenerateLoot` in their own `_Ready`. Two failure modes are silent by
nature and both are guarded: `Node.Set` on a misspelled property does nothing (the manager warns,
and `WorldSceneRegressionTests` fails on an override naming a node the scene doesn't have), and a
node left on its scene-default `SaveId` collides with another destination's (the same test resolves
every destination's *effective* ids and asserts uniqueness).

The console's twice-deferred `ApplyCurrentLocation` is still load-bearing and now for a slightly
different reason — see its comment: the console readies before the manager has instantiated
anything, so the second defer is what lands it behind every destination's own deferred
`SeedDefaultShipLayout`.

### What remains: freeing the destination you left

Every destination is instantiated once and kept; presence is still toggled by `SetShipPresence`. The
only cost is resident greybox geometry, which at eight destinations is not a real cost.

Freeing them is a much larger step than it looks, and it *conflicts with work already done*.
`ShipSim` is a `Node` that owns its `ShipSystems`, so freeing an absent destination destroys the very
sim the coarse LOD tick exists to keep running — an absent ship wouldn't tick slowly, it would cease
to exist. Freeing also drops four things `SaveManager` reads straight off the live tree:
`ShipBuildTarget`'s build state, the procgen `LayoutSeed`, `IStateSaveable`/`ISaveable` door and
console state, and any contract `mission_item` sitting in that wreck.

So its real prerequisite is the **`FleetRegistry`** of the plan's Phase 4 — sim ownership moved off
the node so a destination's state outlives its scene — plus a capture-on-unload cache for the
node-owned save state. Not scene work at all.

## Before editing this subsystem

Any physics-bearing scene must stay within the ≤~10 km bubble bound and re-center near origin — don't let a "just this once" scene span the strategic layer's distances directly.

Adding a **sixth** hand-placed destination group is the point to stop and do the runtime-instancing
work instead — a new one costs a `destinations.json` row *plus* a scene edit and three parallel
NodePath array entries on the travel console.
