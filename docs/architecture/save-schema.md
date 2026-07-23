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

## Known gaps against the rules above

- **"Serialize live sim state, not just static layout" is only partly true.** Saved: structural
  and fixture state, surface/fixture health, battery and thruster charge, suit resources, contract
  `RemainingSeconds`. **Not saved:** per-cell `AtmosphereVolume` (pressure / O₂ / temperature),
  `Deck.Fires`, and any in-flight verb progress (`IVerbTarget.CurrentVerbProgress`). The doc's own
  named example — "a fire mid-spread" — does not survive a round trip. Loading mid-session masks
  this (the live systems simply keep their current values), so it only shows up on
  quit-and-reload, where a vented-and-repaired room comes back at whatever its `_Ready` seeding
  produces rather than what was saved.
- **Ships are not serialized as an explicit list.** They're flat dictionaries keyed by each node's
  `SaveId`, which is functionally per-ship but carries no ship identity, strategic-map location,
  or sim-LOD state — the three things `multi-ship-fleet.md` expects a fleet save to hold. Adding
  them is a schema change (i.e. a v2 + migration), not an additive field.

## Before editing this subsystem

This is explicitly "keep on a short leash" in root `CLAUDE.md` — any change to the save format requires user sign-off, plan mode, and a passing save/load round-trip test before merge.
