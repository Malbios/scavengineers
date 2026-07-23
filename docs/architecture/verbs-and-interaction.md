# Verbs and interaction

Full detail: `docs/project-plan.md` §4 (locomotion note), §5 "Simulation rules", Appendix A8.

## Settled

- **Generalized verb system is core architecture, built from Phase 1** — not an MVP-only stub. Objects expose verbs (install/uninstall/repair/dismantle/scrap/haul/toggle/hack…) with requirements, durations, and outcomes.
- Inventory, salvage, repair, and ship-building all hang off this one system — **don't hardcode a bespoke interaction path per object type.**
- **Placement/building pattern:** placing something creates a placeholder; a task fulfils it over time. This duration is exactly what time-acceleration skips (see `save-schema.md` / plan §5 "Time acceleration").
- **Reach is proximity-limited** (~2 tiles), or the player EVAs to the object.
- Phase 0 Spike 1 builds the first version alongside grounded locomotion: a right-click → action stub, minimal but real.

## Implemented today

`Scripts/Verbs/` — `Verb` is a record of `(Id, LocalizationKey, DurationSeconds)` plus
`Requirements` (each `ItemRequirement` either consumed or merely held, which is how durable tools
like the wrench/drill/crowbar work), an optional runtime `DisplaySuffix`, `Disabled` (shown in red
rather than hidden), and `IsDestructive` (sorted last so a destructive verb is never the default).
`IVerbTarget` exposes `AvailableVerbs` / `CurrentVerbProgress` / `ExecuteVerb` / `CancelVerb`,
plus optional `DisplayNameKey`, `Condition` (drives the PDA health-scan readout) and
`HighlightVisual` (drives scan-mode highlighting). `Player` raycasts, filters by affordability,
cycles with the scroll wheel, runs the duration, and calls `ExecuteVerb` — it never branches on
target type.

Around 15 implementers exist (build target, doors, airlock, battery, thruster, storage, bunk,
ladder, recharge station, damaged conduit, light switch, vendor, contract giver, travel console,
pickups). Panel-opening targets (vendor, contract giver, travel console) go through the same
`ExecuteVerb` path and open a panel from there rather than adding a bespoke input route — the
right pattern.

The **two-tier upkeep model** is the clearest payoff: every wearing thing (surfaces, conduits,
machines, the travel console) exposes Maintain (wrench only, above 50 % health) or Repair (wrench
+ spare parts, at or below 50 %) — one shape reused everywhere, driven by `MaintenanceTier`.

## Known drift

`ShipBuildTarget` is ~2 800 lines and holds roughly nine parallel `switch (MachineType)`
expressions — `InstallVerbFor`, `UninstallVerbFor`, `ScrapVerbFor`, `MaintainVerbFor`,
`RepairVerbFor`, `MachineFixtureIdFor`, `ItemIdFor`, `ScrapYieldFor`, `MachineStateOf` — plus a
hand-maintained static `Verb` field per machine per action. Adding one installable machine means
touching all of them. That is the "bespoke path per object type" the rule above forbids, arrived
at gradually rather than in one decision. The fix is the same one `ItemCatalog.StorageItemIds`
already demonstrates for storage tiers: describe machines as data (item id, fixture id, mount
height/offset, scrap yield, whether it wears) and generate the verb set from it.

## Before editing this subsystem

New interactions should be new verbs (or new requirements/outcomes on existing verbs) — not new bespoke code paths. If a feature seems to need a one-off interaction mechanism, that's a signal to extend the verb system instead.
