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
  `save-schema.md`'s "known gaps". Live sim state itself *is* now saved per ship.
- **Crew-as-agents: vacuously held.** No crew exist, and nothing ties ship simulation to the
  player's presence.
- **Sim LOD: held.** `Scavengineers.Sim.Fleet.ShipSystems` bundles a ship's `Deck` plus its
  atmosphere/power/fire/wear systems as a plain object with no Godot dependency — so a ship genuinely
  can simulate with no scene, which is what this seam always asked for and what the old
  node-owned fields made impossible. `ShipSim` owns one and delegates.
  `TravelConsoleVerbTarget.SetShipPresence` sets `ShipSim.IsPresent`, and an absent ship switches to
  `TickCoarse`: it banks elapsed time and spends it in one-second lumps. Deliberately *not* frozen —
  a derelict left venting is still vented on return, and one left intact has still worn. Freezing
  would make time pass only where the player is looking, which inverts the "presentation skip is not
  a cost skip" rule (plan §5).

  **But the sim is Godot-free while its *ownership* is not.** `ShipSim` (a `Node`) constructs and
  holds the `ShipSystems`, so the state can't outlive the node even though nothing in it depends on
  Godot. That distinction is invisible day to day and decisive the moment anything wants to free a
  ship's scene: freeing it destroys the sim that the coarse tick exists to keep running — an absent
  ship wouldn't tick slowly, it would cease to exist.

  The missing piece is a **`FleetRegistry`**: one entry per ship holding the `ShipSystems` (and the
  node-owned save state, per `save-schema.md`), alive independently of any scene, with `ShipSim`
  demoted to a *view* onto its entry. That is the actual prerequisite for runtime destination
  loading/unloading — not the scene work, which is done (`space-and-travel.md`).

## Explicitly deferred

Crew NPCs with physiological/psychological needs, modular ship construction (tile-by-tile building UI), economy/stations/factions — all postponed well beyond MVP. Don't implement crew AI or fleet UI now; just don't violate the seams above while building single-ship MVP code.
