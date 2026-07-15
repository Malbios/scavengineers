# Scavengineers — Agent Operating Rules

Solo-dev, Windows-first 3D survival/salvage game in Godot 4.7 (Forward+, Jolt physics), C#. A spiritual successor to Ostranauts — mechanics only, never its names, art, lore, or text.

## Source of truth

- `docs/project-plan.md` is the authoritative design document. Read it (or the relevant `docs/architecture/*.md` extract) before proposing or making design-level changes.
- `docs/architecture/*.md` — one file per subsystem, each a short extract of settled decisions plus a pointer back to the plan section. Read the relevant one before editing that subsystem's code.
- This file is the operating rules layered on top of both.

## Non-negotiable architecture rules

- **Simulation / presentation split.** Sim logic (atmosphere, power, hazards, etc.) is plain, headless-testable C# with no Godot node dependencies. Godot nodes are a thin view layer over sim state.
- **Data-driven.** Ships, systems, items, and hazards are data (JSON or `.tres`), loaded at runtime — never hardcoded into scenes.
- **One solver.** Atmosphere, power, rooms, and (later) data/signal networks all read the same flood-fill/graph solver over the ship's structural tier. Don't fork a second one for a new subsystem — extend the shared one.
- **Ship model is three-tier, never voxel.** Structural (grid/edge-snapped: floors, ceilings, walls, machinery footprints), fixtures (surface-attached, free position: conduits, switches, lights), loose objects (free physics transform). Walls live on shared edges — one wall per edge, never `WallA <> WallB` doubling.
- **Verb-driven interaction.** Object interactions (install/uninstall/repair/dismantle/scrap/haul/toggle/hack…) go through the generalized verb system. Don't hardcode a one-off interaction path for a specific object type.
- **Ship is an instance, never a singleton.** The world holds a *list* of ships even with one ship in MVP; the player references "currently occupied ship" as a separate entity. Never reach for a global "the ship."
- **Time acceleration is a presentation skip, not a cost skip.** Fast-forwarding still integrates the full `dt` and pays full costs. Only available when settled (repair/wait/transit); disabled during active real-time hazards/EVA. Real-time physics never time-warps.
- **Saves:** stable, never-reused content IDs; versioned schema with migration functions; missing-ID fallback is a placeholder + log, never a crash; serialize live sim state, not just static layout; ships serialize as a list from v1.

## Coding standards

- **C# for game code.** GDScript is fine only for small `[Tool]` editor-side utility scripts — never for gameplay/sim logic.
- **No F#** in the game itself (fragile/unsupported Godot integration) — keep it to external tooling/side-scripts if used at all.
- Standard .NET naming conventions (PascalCase types/members, camelCase locals/parameters) per `.editorconfig`.

## Keep on a short leash

These require explicit user sign-off, plan mode, and a passing test + editor smoke-check before merging — don't just make the call solo:
- Save/serialization format changes.
- Sim invariants (the shared flood-fill/graph solver, atmosphere/power rules).
- Scene/node relationship changes.

## Asset & legal rules

- Log every model, texture, sound, and AI-generated asset in `docs/asset-provenance.md` (source, license, date) at the time it's added.
- Never use Ostranauts assets, text, data, part names, or UI/store-page trade dress as source material, reference, or AI prompt input.
- Low-poly art style is committed — stay within one consistent asset family/kit rather than mixing.

## Current status

Phase 0 (setup & risk spikes) is complete — all three spikes (grounded movement/interaction, headless atmosphere/power sim, free-float feel) and localization scaffolding are committed. Phase 1 (greybox vertical slice) is well underway: home ship + derelict travel, suit resources, inventory/install/repair/consume, save/load, hull breach and power hazards, airlocks, a station shop, and multi-derelict navigation all exist and work. No formal Phase 1 exit-gate check (`docs/project-plan.md` §7) has been done yet, though functionality already extends past a minimal single-wreck slice.

A real test harness exists and runs today across three projects: `Scavengineers.Sim.Tests` (pure C# sim logic, xUnit), `Scavengineers.Scripts.Tests` (Godot-adjacent C# logic, xUnit), and `Scavengineers.NodeTests` (GdUnit4, Godot node/scene-level tests). Run headless via `GODOT_BIN=<path> dotnet test <project>`.

CI is still intentionally deferred — no `.github/workflows` exists yet. Add one only when explicitly asked.
