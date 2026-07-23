# Scavengineers — Agent Operating Rules

Solo-dev, Windows-first 3D survival/salvage game in Godot 4.7 (Forward+, Jolt physics), C#. A spiritual successor to Ostranauts — mechanics only, never its names, art, lore, or text.

## Source of truth

- `docs/project-plan.md` is the authoritative design document. Read it (or the relevant `docs/architecture/*.md` extract) before proposing or making design-level changes.
- `docs/architecture/*.md` — one file per subsystem, each a short extract of settled decisions plus a pointer back to the plan section. Read the relevant one before editing that subsystem's code.
- `docs/scene-authoring.md` — `.tscn` gotchas, node startup order, and the two headless tools that make scene refactors verifiable. Read before touching any scene file.
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

Phase 0 (setup & risk spikes) is complete — all three spikes (grounded movement/interaction, headless atmosphere/power sim, free-float feel) and localization scaffolding are committed.

Phase 1 (greybox vertical slice) is well past its original shape. Built and working today:

- **Loop:** home ship, 2 stations, 5 derelicts, travel console + travel map + docking minigame, airlocks with real per-side doors and atmosphere bridging.
- **Sim:** per-cell atmosphere with whole-component venting, power grid with a battery budget and ship-wide brownout, conduit fire hazard, passive wear on every fixture and structural surface.
- **Player:** grounded + free-float + thruster EVA + ladder climbing, EVA suit with O2/N2/CO2-filter/battery sub-slots, hunger/thirst/energy/health, death and reload.
- **Interaction:** the generalized verb system across ~15 target types, free-form building (floor/ceiling/wall/conduit/machines/`Extend Floor`), two-tier Maintain/Repair upkeep, installable storage furniture, PDA scan cartridges.
- **Content systems:** data-driven items and ship layouts, a procedural layout generator, multi-deck derelicts, contracts (retrieve/deliver) with deadlines and debt, a station vendor economy, save/load + autosave.

No formal Phase 1 exit-gate check (`docs/project-plan.md` §7) has been done yet. Functionality extends well past a minimal single-wreck slice; the gate is about *felt* tension over a 5–10 minute run, which is a judgement call that hasn't been made.

Known architectural debt worth knowing before you build on it (details in the relevant `docs/architecture/*.md`):

- **A ship's sim is Godot-free but its *ownership* isn't.** `ShipSim` (a `Node`) holds the `ShipSystems`, so ship state can't outlive its scene. Destinations are instantiated from `Data/destinations.json` at startup and then *kept*, never freed — freeing one would destroy the sim its own coarse LOD tick exists to keep running, and drop the build state, layout seed and mission items `SaveManager` reads off the live tree. The fix is a `FleetRegistry` with `ShipSim` demoted to a view; that, not scene work, is the prerequisite for runtime load/unload. See `docs/architecture/multi-ship-fleet.md`.
- **`Scripts/Verbs/ShipBuildTarget.cs` (~2 800 lines) is the remaining god class.** Its pieces are genuinely entangled — 37 `[Export]` resources and 7 placement dictionaries that every candidate collaborator reads — so a split there needs a real seam, not just a new file. `Player.cs` was decomposed (2 422 → 1 958) into `PlayerHudView` / `PanelController` / `InventoryWindowView`; follow that pattern's *rule*, not its shape: split where the data boundary is real.
- Verb progress isn't serialized. Deliberate — every verb is ~0.6 s and button-held; revisit with time acceleration, not before.

A real test harness exists and runs today across three projects: `Scavengineers.Sim.Tests` (pure C# sim logic, xUnit), `Scavengineers.Scripts.Tests` (Godot-adjacent C# logic, xUnit), and `Scavengineers.NodeTests` (GdUnit4, Godot node/scene-level tests). Run headless via `GODOT_BIN=<path> dotnet test <project>`. All three pass as of 2026-07-23 (86 / 1118 / 258).

**Verifying NodeTests properly:** most node startup here happens in a `CallDeferred` from `_Ready`, and `SceneTree.PhysicsFrame` fires *before* any `_PhysicsProcess` — so tests that await a fixed number of frames and then assert are racing, and they win on an idle machine. Wait on a condition instead (`Scavengineers.NodeTests/FrameWait.cs`). To surface this class of bug at all, force a rebuild before each run (`(Get-Item <some .cs>).LastWriteTime = Get-Date`) and run 4-6 times; a few clean back-to-back runs prove nothing. Full ordering rules in `docs/scene-authoring.md`.

**Scene edits are not covered by any of that** — nothing in the test suite loads `World.tscn`. Digest the scene before and after with `Tools/scene_digest.gd` and diff; `Tools/world_smoke.gd` checks what actually gets built at runtime. Both headless, both in `docs/scene-authoring.md`.

CI is still intentionally deferred — no `.github/workflows` exists yet. Add one only when explicitly asked.
