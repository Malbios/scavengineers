# Multi-ship / fleet play

Full detail: `docs/project-plan.md` §4 (deferred features), §5 "Multi-ship seams", §10 "Complete-scope north star."

## Status

**Not MVP.** The complete-game north star is the player owning a fleet of ships with NPC crew operating vessels the player isn't aboard, simulating in the background. None of that is built now — but four cheap architectural seams are preserved from day one so it isn't a rewrite later.

## Settled seams (build these in now, even trivially)

- **A ship is an instance, never a singleton.** The world holds a *collection* of ships; the player is a separate entity referencing its "currently occupied ship." The moment "the ship" becomes a global, multi-ship becomes a painful retrofit.
- **Serialize ships as a list from v1**, even with one member (see `save-schema.md`).
- **Sim must be able to downgrade to a level-of-detail.** Full fidelity for the loaded/bubble ship; a cheaper abstracted tick for off-screen owned ships (crew consume O2 at averaged rates, systems degrade on coarse timers). Don't build the LOD tick early — just shape the sim so it *can* run coarse later. Reference pattern: RimWorld/Dwarf Fortress off-map handling.
- **Crew are agents assigned to a ship, not to the player.** A crew member has an assignment (ship + role) and acts against that ship's sim state regardless of the player's camera or loaded scene. The only rule now: never tie crew behavior to the player's presence, even before crew AI exists.

## Seam status today

- **Ship-as-instance: held.** There is no global "the ship." Eight ship *sites* coexist (Home
  Ship, 2 Stations, 5 Derelicts) across 13 `ShipSim` deck instances; the player carries a
  `ShipSimRef` that is re-resolved at runtime from whichever `ShipAtmosphereZone` it's standing
  in, and `Scavengineers.Sim.ShipModel.Ship` is a list of decks from v1.
- **Ships-as-a-list in the save: partly held.** Per-ship state is keyed by `SaveId` in flat
  dictionaries rather than an explicit ship list, and carries no location or LOD field — see
  `save-schema.md`'s "known gaps".
- **Crew-as-agents: vacuously held.** No crew exist, and nothing ties ship simulation to the
  player's presence.
- **Sim LOD: not held, and now actually needed.** All 13 `ShipSim`s tick full-fidelity
  atmosphere + wear + fire from `_PhysicsProcess` every frame, whether or not the player is
  aboard — `TravelConsoleVerbTarget.SetShipPresence` hides and decollides an absent
  ship but doesn't stop it processing. The sim *can* run coarse (it's a plain `Tick(dt)` over
  headless C#, exactly as the seam intended); nothing calls it that way yet. This is the cheapest
  place the LOD work could land, and the reason to do it is now performance, not fleet play.

## Explicitly deferred

Crew NPCs with physiological/psychological needs, modular ship construction (tile-by-tile building UI), economy/stations/factions — all postponed well beyond MVP. Don't implement crew AI or fleet UI now; just don't violate the seams above while building single-ship MVP code.
