# Save schema

Full detail: `docs/project-plan.md` §5 (Save/load row), §5 "Multi-ship seams", Appendix A8.

## Settled

- **Versioned schema + migration functions** from the start — a version field on every save, with migration functions to bring old saves forward.
- **Stable, never-reused content IDs.** Once an ID is assigned to a piece of content, it is never reassigned to something else, even after that content is removed.
- **Missing-ID fallback is a placeholder + log, never a crash.** A save referencing an ID that no longer exists (removed content, disabled mod) must degrade gracefully.
- **Serialize live sim state, not just static layout** — in-flight timers and partial states (a repair task mid-completion, a fire mid-spread) must survive save/load, not just the ship's static structure.
- **Ships serialize as a list from v1**, even with exactly one ship in MVP. This is a five-minute decision now versus a save-schema migration later if skipped (see `multi-ship-fleet.md`).
- **Serialization shape (Appendix A8):** per-tile structural contents + per-edge/boundary states (type + condition) + fixture list `(id, tile, surface, free-position, condition, state)` + loose-object transforms. A save holds a *list* of ships, each tagged with strategic-map location and sim LOD state.
- **Networks (atmosphere/power/rooms) are recomputed on load**, not trusted from the save — the save stores structural/fixture state, not derived connectivity.
- **Modding depends on this:** PCK/ZIP resource packs + data overrides are "nearly free" if content stays data-driven and IDs stay stable — don't undermine that by hardcoding content references.

## Implemented today

`Scripts/SaveLoad/` — `SaveManager` (F5 save / F9 load, plus a 300 s autosave timer) writes
`SaveData` as JSON to `user://savegame.json`. Nodes opt in by joining the `saveable` group and
implementing one of four interfaces: `ISaveable` (a bool), `IStateSaveable` (a string),
`IBuildTargetSaveable` (a whole `BuildTargetSaveData`), `IShipLayoutSaveable` (a procgen seed).
Loose world objects (dropped containers, contract mission items) are captured by group scan and
cleared-and-respawned on load.

Rules genuinely held:

- Versioned (`SaveData.CurrentVersion`, still 1 — every change so far has been additive, with
  each new field carrying a "missing = this default" comment). `Load` has the migration dispatch
  point stubbed and warns rather than refusing.
- Missing-ID fallback is a warning, never a crash (`SaveManager.Load`'s unknown-id loop,
  `ItemCatalog`/`ShipLayoutCatalog`'s null/default returns).
- Networks are never serialized — `Deck`/`AtmosphereSystem`/`PowerSystem` are rebuilt from
  structural + fixture state on load.
- Per-tile / per-edge shape as described above: conduits, wall conduits, walls, floor/ceiling
  breaches, extended cells, per-surface health entries, per-conduit conditions, machines.

- **Live sim state is saved** via `IShipStateSaveable` → `SaveData.Ships`, keyed by `ShipSim.SaveId`
  (all 13 decks in `World.tscn` carry a stable one). Each entry holds every cell's
  `AtmosphereVolume` plus the burning-cell set. Applied *after* `ApplyBuildState`, deliberately:
  build state reconstructs walls and breaches, which decide whether a room is open to vacuum, so
  restoring air first would let the very next tick re-vent rooms the save had already repaired.
  Fires are replaced wholesale, so a save with none genuinely puts a burning ship out.

## Known gaps against the rules above

- **In-flight verb progress is not saved**, deliberately for now: every verb is ~0.6 s and
  button-held, so persisting a fraction of one is meaningless. The rule is really about
  long-duration background tasks, which don't exist until time acceleration does — revisit
  alongside that feature, not before.
- **Ships are not serialized as an explicit list.** They're flat dictionaries keyed by `SaveId`,
  which is functionally per-ship but carries no strategic-map location or sim-LOD state — two of the
  things `multi-ship-fleet.md` expects a fleet save to hold. Adding them is a schema change (a v2 +
  migration), not an additive field.

## Before editing this subsystem

This is explicitly "keep on a short leash" in root `CLAUDE.md` — any change to the save format requires user sign-off, plan mode, and a passing save/load round-trip test before merge.
