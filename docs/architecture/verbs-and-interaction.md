# Verbs and interaction

Full detail: `docs/project-plan.md` §4 (locomotion note), §5 "Simulation rules", Appendix A8.

## Settled

- **Generalized verb system is core architecture, built from Phase 1** — not an MVP-only stub. Objects expose verbs (install/uninstall/repair/dismantle/scrap/haul/toggle/hack…) with requirements, durations, and outcomes.
- Inventory, salvage, repair, and ship-building all hang off this one system — **don't hardcode a bespoke interaction path per object type.**
- **Placement/building pattern:** placing something creates a placeholder; a task fulfils it over time. This duration is exactly what time-acceleration skips (see `save-schema.md` / plan §5 "Time acceleration").
- **Reach is proximity-limited** (~2 tiles), or the player EVAs to the object.
- Phase 0 Spike 1 builds the first version alongside grounded locomotion: a right-click → action stub, minimal but real.

## Before editing this subsystem

New interactions should be new verbs (or new requirements/outcomes on existing verbs) — not new bespoke code paths. If a feature seems to need a one-off interaction mechanism, that's a signal to extend the verb system instead.
