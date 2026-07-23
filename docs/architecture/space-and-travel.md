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

**Why it stopped here.** Derelicts are already a shared `Derelict.tscn` and could be instanced at
runtime tomorrow. Stations can't: both are authored inline in `World.tscn`, and the `Station` block
references 32 `SubResource`s and 10 `ExtResource`s from that file's shared header. Extracting it
into its own scene means relocating or duplicating all of those and remapping the ids — and
**nothing in the test suite loads `World.tscn`**. `Scavengineers.NodeTests` is a separate,
scene-less Godot project; `WorldSceneRegressionTests` reads the file as *text*. The only real check
is `godot --headless --quit`, which catches an unresolved resource but not a mesh silently pointing
at the wrong `SubResource` of the right type. That is the "locally plausible but globally wrong"
failure mode `docs/project-plan.md` §6 warns about, so it wants the Godot editor's own
scene-extraction tooling rather than blind text surgery.

**The order to do it in:** extract `Station.tscn` in the editor first (verify visually, commit on
its own), then instancing is a contained change — `DestinationCatalog` gains a `scene` field, a
`BubbleRoot` replaces the fixed groups, and the travel console's NodePath arrays go away. Per-ship
sim state already survives outside a live node (`ShipSystems` + `SaveData.Ships`), so the
save-loses-absent-destinations hazard that would otherwise come with instancing is already handled.

## Before editing this subsystem

Any physics-bearing scene must stay within the ≤~10 km bubble bound and re-center near origin — don't let a "just this once" scene span the strategic layer's distances directly.

Adding a **sixth** hand-placed destination group is the point to stop and do the runtime-instancing
work instead — a new one costs a `destinations.json` row *plus* a scene edit and three parallel
NodePath array entries on the travel console.
