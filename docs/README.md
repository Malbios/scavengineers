# Docs index

- **`project-plan.md`** — the authoritative design document: scope, technical decisions, art strategy, phased roadmap, ship/sim architecture. Read this before making any design-level decision.
- **`architecture/`** — one short file per subsystem (locomotion, ship model, atmosphere/power sim, verbs, save schema, space/travel, multi-ship/fleet, localization), each a settled-decisions extract of `project-plan.md` with a pointer back to the full section. Read the relevant one before editing that subsystem's code.
- **`asset-provenance.md`** — register of every model/texture/sound/AI-generated asset (source, license, date). Update it the same day an asset is added.

Root `CLAUDE.md` (repo root, one level up) holds the operating rules layered on top of all of this — read it first.
