# Multi-ship / fleet play

Full detail: `docs/project-plan.md` §4 (deferred features), §5 "Multi-ship seams", §10 "Complete-scope north star."

## Status

**Not MVP.** The complete-game north star is the player owning a fleet of ships with NPC crew operating vessels the player isn't aboard, simulating in the background. None of that is built now — but four cheap architectural seams are preserved from day one so it isn't a rewrite later.

## Settled seams (build these in now, even trivially)

- **A ship is an instance, never a singleton.** The world holds a *collection* of ships; the player is a separate entity referencing its "currently occupied ship." The moment "the ship" becomes a global, multi-ship becomes a painful retrofit.
- **Serialize ships as a list from v1**, even with one member (see `save-schema.md`).
- **Sim must be able to downgrade to a level-of-detail.** Full fidelity for the loaded/bubble ship; a cheaper abstracted tick for off-screen owned ships (crew consume O2 at averaged rates, systems degrade on coarse timers). Don't build the LOD tick early — just shape the sim so it *can* run coarse later. Reference pattern: RimWorld/Dwarf Fortress off-map handling.
- **Crew are agents assigned to a ship, not to the player.** A crew member has an assignment (ship + role) and acts against that ship's sim state regardless of the player's camera or loaded scene. The only rule now: never tie crew behavior to the player's presence, even before crew AI exists.

## Explicitly deferred

Crew NPCs with physiological/psychological needs, modular ship construction (tile-by-tile building UI), economy/stations/factions — all postponed well beyond MVP. Don't implement crew AI or fleet UI now; just don't violate the seams above while building single-ship MVP code.
