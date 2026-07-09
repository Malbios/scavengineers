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

## Before editing this subsystem

This is explicitly "keep on a short leash" in root `CLAUDE.md` — any change to the save format requires user sign-off, plan mode, and a passing save/load round-trip test before merge.
