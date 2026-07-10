# Project Plan — A 3D Ostranauts-like (solo edition)

*Working codename: **Project Derelict** (pick your own before anything is public).*

This plan takes the mechanics of Ostranauts — derelict salvage, systems-driven survival, a ship you maintain as a living machine — and rebuilds them as an original 3D game in Godot. No art, lore, names, writing, or data from Ostranauts are reused. This is a **spiritual successor**, which the source report also flags as the safest and most finishable path.

It is deliberately re-scoped for **one person**. The report's "spiritual successor" estimate (22–40 person-months, 3–5 people) is not a solo target. The plan below is built around the one metric that actually matters for a solo dev: **reaching a playable, evaluable loop as fast as possible, then deciding whether to keep going.**

---

## 1. Guiding principles

1. **Ship a small finished thing over a large unfinished thing.** The core loop is the product. Everything else is optional depth added *after* the loop is proven fun.
2. **Greybox first, art last.** Systems are validated with cubes and capsules. Art is the single biggest solo bottleneck, so it is deferred until the game is provably worth dressing up.
3. **Data-driven from day one.** Ships, systems, items, and hazards live in data files, not hardcoded scenes. This is cheap to set up early and brutally expensive to retrofit later. It also gives you moddability nearly for free.
4. **Vertical slice is the go/no-go gate.** After the first playable loop exists, you make an honest decision: continue, pivot, or shelve. No multi-year commitment before that point.
5. **Cut relentlessly.** Every feature is guilty until proven essential to the core loop.

---

## 2. Key technical decisions

### Language: **C#**, not GDScript

The report recommends typed GDScript for most gameplay. That advice is correct *for a team building around Godot's grain*. For you it's the wrong call. You have deep C# (and F#) expertise and only some Godot experience — so the smart move is to spend your "new things to learn" budget on **Godot's 3D stack**, not on a new scripting language on top of it. C# lets you lean on Rider/VS tooling, strong static typing across a systems-heavy codebase, and a .NET ecosystem you already know for serialization, data, and tests.

Honest caveats you should expect:
- Godot's docs and community examples are GDScript-first; you'll be translating in your head constantly.
- C# has a compile step, so editor iteration is slightly slower than GDScript's hot-reload.
- `[Tool]` scripts and small editor plugins are more awkward in C#. When you need a quick editor-side utility, just write it in GDScript — mixing languages per-node is fine and normal.
- Heavy per-frame Godot API calls from C# incur marshalling overhead. This is a *profile-later* concern, not an MVP blocker.

**On F#:** I know it's your home language, but resist using it here. F# on Godot is technically possible via the .NET runtime but the integration is fragile, undocumented, and unsupported — exactly the kind of yak-shaving that kills solo projects. Keep F# for tooling/side-scripts if you must; build the game in C#.

### Engine: **Godot 4.x (4.6 or 4.7)**

Agrees with the report. Use the current stable branch, Forward+ renderer for desktop, and the built-in **Jolt** physics backend (default in 4.4+) since this is a rigid-body-heavy game.

### Scope: **partial-remake loop → spiritual successor**, but with a much smaller MVP than the report's

The report's phase plan is sound in *shape*; the *size* of each phase must shrink dramatically. See §4 for the actual MVP definition.

### Platform: **Windows desktop only** for MVP

Linux/Steam Deck later if it's going well. No web, no mobile, no console. Godot makes desktop exports trivial, so this costs you nothing to keep open, but you commit to none of it up front.

---

## 3. Art strategy — **committed: low-poly**

For a solo systems programmer, **art is the real project risk, not code.** You can architect a survival sim; you probably can't hand-model a convincing photoreal space station in your spare time. The decision here is therefore **low-poly**, and it's the right default for this project — not a compromise.

**Why low-poly is the correct call (not just the easy one):**

- **It lets you skip the expensive stages, not just reduce them.** High-fidelity 3D punishes you at every step: high-res textures, normal maps, careful UV unwrapping, full PBR materials, and topology where every flaw shows under close inspection. Low-poly collapses most of that — flat or simple colors, minimal texturing, forgiving topology. When the bottleneck is *you*, cutting per-asset cost is what decides whether the game ships.
- **It reads as a deliberate style, not "unfinished."** This is the trap with semi-realistic solo art: it looks like a worse version of a AAA game and invites the "why doesn't this look like Star Citizen" comparison. A committed low-poly look is legibly *a choice*, so a rough model reads as intentional rather than broken.
- **It fits your setting for free.** Cramped, industrial, dimly-lit ship interiors are extremely forgiving for low-poly. Dark corridors, strong pools of light, grime and shadow hide simple geometry beautifully — and that's exactly the atmosphere the game wants anyway.

**What to keep in mind so it actually works:**

- **The saved time moves into lighting, palette, and composition — not away entirely.** Good low-poly games live in the lighting pass and a tight, consistent colour palette far more than in the meshes. This is a *good* trade for you: lighting is iterative and programmer-friendly, whereas modeling is slow manual craft.
- **Consistency beats fidelity.** Low-poly looks great when everything shares one density, palette, and material language, and looks amateur the instant a detailed bought asset sits next to a blocky one. Stay inside a single visual family.
- **Modular kit, not bespoke scenes.** Build interiors from a small library of tiling wall/floor/prop pieces. One good corridor kit tiles into infinite ships — and this pulls in the same direction as the consistency rule above.
- **Asset packs, licensed carefully, one family at a time.** Synty is the obvious purpose-built low-poly source; itch.io, Kenney, and glTF-compatible marketplace kits also work. Pick one family and stay in it. Keep an **asset provenance register** (source, license, date) from file #1 — legal insurance, seconds per asset.
- **AI for textures/decals/icons, not models.** AI-generated grunge textures, decals, and UI icons are fine and useful; AI-generated *geometry* isn't game-ready. Use kits/kitbashing for models.
- **Audio punches above its cost.** Low-poly + moody lighting + good sound is a combination that looks and feels far more expensive than it was to make. Budget real time for sound.

**One honest caveat:** low-poly lowers the art bar, it doesn't remove it. You still need enough taste to keep the palette and lighting coherent — careless low-poly (clashing colours, flat lighting, mismatched densities) can look worse than nothing. But as a way to make the art *achievable solo without looking cheap*, it's the right decision.

Pipeline: **Blender → glTF 2.0 → Godot** (report agrees). Even if you're buying kits, you'll want Blender for kitbashing and export fixes.

---

## 4. The MVP core loop

Strip Ostranauts to its beating heart. The signature fantasy is: *a lone scavenger keeping a fragile ship alive while looting dead ones in a hostile vacuum.* Everything in the MVP serves that sentence.

**The loop:**

1. **Home ship** — a small fixed ship you live on, with survival systems you maintain: oxygen/atmosphere, power, and hull integrity. *(One ship in MVP — but modeled as an instance in a fleet of one, never a hardcoded singleton; see §5 multi-ship seams.)*
2. **Approach a derelict** — arrive at a wreck (via a simplified travel step, *not* full piloting yet).
3. **Suit up and board** — the derelict has no atmosphere / is dark / has hazards. Your suit has limited O2 and battery.
4. **Salvage** — pull components, resources, and items. Emergent hazards make it tense: hull breaches, dead power, debris, maybe an electrical fire.
5. **Return** — bring loot back to your ship.
6. **Maintain & upgrade** — use salvage to keep your own systems running (replace a failing O2 scrubber, patch a breach, recharge cells) and improve capacity.
7. **Move on** — travel to the next, harder/richer wreck. Repeat, with a survival-pressure ratchet.

That is a complete, tense, replayable loop. It's shippable on its own as a small game.

**In MVP:**
- Systems-driven survival on the home ship (O2, power, hull) with **diegetic interaction** (flip a breaker, patch a hull tile, swap a component) — this is the soul of the game.
- **Grounded (mag-boot) locomotion for the loop**, first-person. Free-float and thruster EVA are layered in after the grounded loop works (see locomotion note below); third-person is optional later.
- Suit resources (O2, power) as the exploration pressure.
- A handful of hazard types.
- A basic inventory + install/repair/consume flow for salvage.
- Save/load.
- Data-driven ships, systems, items, hazards.

**Deferred to post-MVP phases (explicitly NOT in the first loop):**
- Full Newtonian *ship* piloting and docking (flying the whole vessel). MVP uses a simplified/abstracted travel step instead. This defers *flying the ship*, not moving on foot — on-foot movement (grounded mag-boot walking) is in MVP.
- Crew NPCs with physiological/psychological needs. *Postponed well beyond MVP — the easiest big system to defer.* Keep only the architectural seam (crew-as-agents, §5); zero NPC implementation early. Interim multi-ship, if wanted before crew exist, is just the player moving between their own parked ships.
- Modular ship *construction* (tile-by-tile building). *Start with a fixed ship + a few upgrade slots.*
- **Character-creation lifepath** (Traveller / Ostranauts-style front-end that outputs your starting character, ship, gear, and debt). A real identity feature — but a scope sink and *outside* the core loop. Hardcode a fixed starting setup for early greybox; add a **minimal** lifepath later; only deepen it once the loop underneath is proven.
- **Multi-ship ownership & fleet play** (part of the *complete* scope, not MVP). The player eventually owns several ships and can move between them, with NPC crew operating vessels the player isn't aboard — ships simulating in the background. Not built early, but the architecture must leave room for it from day one (see §5 multi-ship seams: ship-as-instance, fleet-as-list, sim LOD, crew-as-agents).
- Economy, stations, factions, quests. *Content — intentionally undefined this early (see §10).*

**Locomotion.** Build **mag-boot grounded walking first** (Phase 0–1). Movement is decoupled from the sim/build systems, so a grounded baseline lets you validate building, salvage, atmosphere, power, and the verb system without the hardest movement system gating the greybox. The **complete** game runs all three modes, chosen by context: mag-boots in gravity/on hull, free-float in zero-g interiors and derelicts, thruster EVA in open vacuum. What the player *earns* over the campaign is **precise maneuvering** (thruster control, stabilizers) over raw drift, not the ability to move. Free-float gets an isolated Phase-0 feel spike and is layered into the loop after the grounded loop works. First-person throughout; third-person optional later. Comfort options (vignette, auto-orient, reference-"up") ship as settings once float exists.

---

## 5. Tech stack & architecture

| Area | Choice | Notes |
|---|---|---|
| Engine | Godot 4.6/4.7, Forward+ | Desktop target |
| Language | C# (.NET) | GDScript only for small editor tools |
| Physics | Jolt (built-in) | Rigid-body heavy; treat docking/EVA/loose-parts as prototype-first risks |
| Data | JSON or Godot `.tres` resources | Ships/systems/items/hazards defined as data, loaded at runtime |
| Save/load | Versioned schema; serialize *live sim state* | Version field + migration fns; stable never-reused content IDs; missing-ID fallback (placeholder + log, never crash); save in-flight timers/partial states, not just static layout |
| Modding | PCK/ZIP resource packs + data overrides | Nearly free if you go data-driven early; a great differentiator. Depends on the stable-content-ID rule above |
| Localization | String tables from commit #1 | No hardcoded display text; German + English likely. Cheap now, painful to retrofit |
| Assets | Blender → glTF 2.0 | Plus licensed kits, provenance-tracked |
| Tests | GUT or GdUnit4 + xUnit for pure C# logic | Test the *simulation logic* separately from scenes |

**Architectural notes for a systems dev:**
- Separate **simulation from presentation.** Model the atmosphere/power/hull sim as plain C# that can run and be unit-tested headless, with Godot nodes as a thin view layer. You already think this way from your functional-architecture work — lean into it. It makes the sim testable, deterministic, and moddable, it's the single best defense against the "locally plausible but globally wrong" bugs the report warns AI agents introduce, **and it's the load-bearing requirement for multi-ship (below)** — a ship the player isn't aboard has no loaded scene, so its sim must run without one.
- **Entity/component or data-table style** for ship systems rather than deep node inheritance. A power grid, an atmosphere volume, and a hull-tile grid are data structures first, scenes second.
- **Deterministic sim tick** if you can manage it — makes replay tests and save/load far more reliable.

**Multi-ship seams (design now, build later).** The complete vision (§4) has the player owning a *fleet* with NPC crew operating ships the player isn't on. That's not MVP, but four cheap decisions today stop it becoming a rewrite:
- **A ship is an instance, never a singleton.** The world holds a *collection* of ships; the player is a separate entity referencing its "currently occupied ship." The moment "the ship" becomes a global, multi-ship is a painful retrofit.
- **Serialize ships as a list from v1**, even with one member. A five-minute choice now; a save-schema migration later if skipped.
- **Sim must downgrade to a level-of-detail.** Full fidelity for the loaded ship; a cheaper abstracted tick for off-screen owned ships (crew consume O2 at averaged rates, systems degrade on coarse timers). Don't build it early, but shape the sim so it *can* run coarse. (RimWorld/Dwarf Fortress off-map handling is the reference pattern.)
- **Crew are agents assigned to a ship, not to the player.** A crew member has an assignment (ship + role) and acts against that ship's sim state regardless of the player's camera or loaded scene. Slots into the deferred crew-AI work; the only rule now is: never tie crew behavior to the player's presence.

**Space representation — two layers (settled).** To get "infinite, seamless" space without float-precision jitter:
- **Strategic layer:** a map/graph of locations (stations, wreck fields, POIs) — pure data, no physics, arbitrarily huge. This is where felt scale lives.
- **Tactical layer ("bubbles"):** bounded, origin-local physics scenes instantiated around wherever you are — a transit bubble (your ship at origin) or an encounter bubble (target + your ship). Always re-centered near origin, always small (≤~10 km) so floats never jitter.
This delivers seamless-*feeling* travel with transitions (not literally-continuous cross-universe flight, which is the Star Citizen streaming rabbit hole). Bonus: **a bubble is the natural unit of "fully simulated"** — ships in the current bubble run full fidelity, everything else runs the coarse multi-ship LOD tick. One concept, two payoffs.

**Ship representation — three placement tiers, not voxels (settled; full detail in Appendix A).** A 2D tile grid per deck (1 m tiles), decks stacked for verticality. Placement splits into three tiers: **structural** (floors/ceilings per tile, walls per shared edge, machinery footprints — grid-snapped, drive the sim), **fixtures** (conduits/switches/lights — attached to a surface at a free position, drive the networks), and **loose objects** (items/cargo — free physics transforms that float in zero-g). Atmosphere, power, data, and room-detection all run on **one shared flood-fill/graph solver** reading the structural tier. Conduits are physical fixtures that spark/ignite when powered + damaged + O2 present. Full free building via a generalized verb system, proximity-limited reach.

**Simulation rules (settled):**
- **Atmosphere = lumped per-volume, never per-tile CFD.** Each sealed volume holds scalars (pressure, O₂ fraction, temperature); breaches equalize between volumes or to vacuum at a rate. Cheap, time-warp-stable, reuses the room flood-fill, and fully sells the fantasy. This ceiling is deliberate — do not let it "improve" into a fluid sim.
- **Time acceleration = presentation skip, not cost skip.** Fast-forwarding a long task advances the full clock and pays the full bill over that span (fatigue, hunger, suit O₂, power all tick; the character can't multitask). "Felt as hard work" comes from the consequences. Available only when *settled* (repair/wait/transit); **disabled during active real-time moments** (live fire, breach, EVA piloting). Every abstract sim integrates over `dt`; real-time physics never time-warps.
- **Generalized verb system is core architecture, built from Phase 1.** Objects expose verbs (install/uninstall/repair/dismantle/scrap/haul/toggle/hack…) with requirements, durations, and outcomes. Inventory, salvage, repair, and ship-building all hang off it — don't hardcode interactions per-object.
- **Crew navigation stays grounded.** NPC pathfinding assumes gravity/mag-boot interiors (standard navmesh). Do not promise crew doing free-float EVA pathfinding — that's a research problem.

---

## 6. AI coding-agent workflow (Claude Code)

You're already working in Claude Code, so lean on it — but deploy it where it's strong and keep a human hand on the tiller where the report says it matters (architecture, profiling, design/balance, integration, legal). For a solo dev the agent is a force-multiplier on volume; your job is to stay the architect.

**Set up once:**
- A `CLAUDE.md` (and/or `AGENTS.md`) at the repo root: coding standards, the sim-vs-presentation split, naming conventions, the test command, "never touch save-schema/serialization without asking," and asset/licensing rules.
- Per-subsystem `docs/architecture/*.md` the agent reads before editing (locomotion, atmosphere, salvage, save).
- Hooks/skills for repeatable loops: run tests → export headless build → capture a screenshot → report. You've already been exploring Claude Code hooks, so this is familiar ground.

**Best-fit tasks (high ROI):** scaffolding scenes/nodes, data schemas + importers, editor tooling, test harnesses and replay scenarios, refactors, shader/material helpers (visually verify every one).

**Keep on a short leash:** anything touching save format, sim invariants, or scene relationships. Use plan-mode first, narrow file scope, and require a passing test + editor smoke-check before you merge. Merge conservatively.

**Prompt shape that works** (from the report, and it's good advice): state the goal → point at the files/docs to read first → list explicit acceptance criteria → define how to validate → constrain what *not* to touch.

---

## 7. Phased roadmap

Milestone-based, not date-based — solo timelines are too variable for hard dates. Rough calendar ranges assume steady part-time work; halve them if you go full-time. Each phase has a hard **exit gate**; don't advance until it's met.

### Phase 0 — Setup & risk spikes  *(~2–4 weeks)*
Prove the scary bits before committing.
- Repo, C#/Godot 4.x project, CI (headless export + tests), `CLAUDE.md`, asset-provenance register, string-table/localization scaffolding.
- **Spike 1 — grounded movement + interaction:** first-person **mag-boot walking** in a greybox ship, plus the generalized **verb/interaction** stub (right-click → action). This is what the greybox loop runs on.
- **Spike 2 — headless sim:** the shared flood-fill/graph solver in pure C# doing minimal atmosphere + power (a breach vents O2; an open switch cuts power to a region) with unit tests. Load-bearing for everything.
- **Spike 3 — float feel (isolated, de-risk only):** a throwaway free-float push-off prototype to check comfort/feel. *Not* wired into the loop yet; informs whether the long-term float layer is viable.
- **Exit gate:** grounded traversal + interaction feel right; the headless sim passes tests; the float spike isn't a comfort disaster; no catastrophic tech blocker.

### Phase 1 — Greybox vertical slice  *(~2–4 months)* ← **the real go/no-go**
The whole core loop, entirely in cubes and capsules.
- One home ship, one derelict, the loop from §4 steps 1–6: board → salvage → return → maintain. Step 7 ("move on to the next, harder wreck," and the survival-pressure ratchet) is Phase 2 content — Phase 1 tests a single wreck run, not a sustained multi-wreck session.
- **Mechanics existing is not the same as content that takes time to get through.** Suit pressure, a hazard, verbs, and inventory are all quick, mechanical interactions on their own — a two-room derelict with three pickups clears in 2–3 minutes no matter how well each mechanic works, because there's too little physical space and too few decisions to make. Phase 1 therefore also needs real scale: a derelict layout big enough (multiple rooms, real travel distance, enough salvage that carry capacity/priority is an actual choice) that navigating and handling it *is* the content, not padding around a handful of instant interactions.
- At least two hazard types that can genuinely threaten the player on their own — not an upkeep mechanic (like a power switch gating a recharge station) relabeled as a hazard for the sake of hitting a number. Plus inventory + install/repair/consume, and save/load.
- **Exit gate:** a single wreck run — suit up, navigate a derelict with real spatial extent, face at least one hazard, make actual salvage decisions, return, patch up — takes a genuine 5–10 minutes of engaged (not padded) play and feels tense, not just functional. This is deliberately a lower bar than a full session: Phase 1 is one wreck, not a campaign.

### Phase 2 — Systems depth  *(~2–4 months)*
Make the sim rich enough to generate emergent stories.
- Deeper interacting systems (temperature, electrical faults → fire, cascading failures), more hazard/salvage variety, meaningful upgrade tree for the home ship, difficulty/reward ratchet across wrecks.
- **Exit gate:** emergent "oh no" moments happen without being scripted, and a 15–30 minute session spanning 2+ wrecks holds up as tense and fun end-to-end — this is where the original full-session bar actually belongs, once a second wreck and the ratchet exist to justify a longer sit.

### Phase 3 — Art & feel pass  *(~2–4 months)*
Only now does it stop being cubes.
- Commit to the aesthetic, build/buy the modular kit, lighting pass, audio pass, UI pass (diegetic where it helps, 2D overlay where it doesn't — don't be dogmatic).
- **Exit gate:** a stranger watching a clip understands what the game is and finds it appealing.

### Phase 4 — Content & progression  *(~3–6 months)*
Turn the toy into a game with a shape.
- Procedural or handcrafted wreck variety, the campaign progression/meta-loop fleshed out (upgrade tree, escalating survival/economic pressure), and the simplified travel layer maybe upgraded toward real ship piloting *if* it earns its cost.
- **Exit gate:** a complete play arc exists start to finish.

### Phase 5 — Polish, mod docs, ship  *(~2–4 months)*
- Performance pass (profile navigation, physics interiors, sim tick, UI redraw — the report's named hotspots), save-migration tests, options/accessibility, storefront assets, mod documentation, and a final legal skim of name/capsule/UI before public reveal.

**Reality check:** a genuinely shippable version of this is a **1.5–3 year part-time** effort. That's fine — but it's why Phases 0 and 1 exist as cheap off-ramps. Get to the fun-or-not verdict inside a few months, before you've spent a year.

---

## 8. Risk register (solo-focused)

| Risk | Likelihood | Mitigation |
|---|---|---|
| **Scope creep** (the classic solo killer) | High | Every feature must serve the §4 sentence; defer aggressively; the vertical-slice gate is sacred |
| **Art bottleneck** | High | Greybox-first, stylized look, licensed modular kits, provenance register; lean on lighting/audio |
| **Free-float feel / comfort doesn't land** | Medium | A layered feature, not the foundation — de-risked in an isolated Phase-0 spike; the grounded loop doesn't depend on it; comfort options as settings; accept intended disorientation, tune out quit-inducing nausea |
| **3D + Godot-3D is new territory for you** | Medium | Spend your learning budget here (not on a new language); small spikes before big commitments |
| **Motivation/burnout over a multi-year solo build** | Medium-High | Short milestones with visible payoffs; the Phase 1 gate gives an early "this is real" hit; permission to shelve |
| **AI agent introduces subtle sim/save regressions** | Medium | Sim-vs-presentation split, headless unit tests, versioned saves, conservative merges, plan-mode |
| **Physics edge cases** (docking, loose parts, breaches) | Medium | Treat as prototype-first; Jolt behaves differently from Godot Physics in joints/margins — test, don't assume |

---

## 9. Legal guardrails (light, since you're already committed to originality)

You've already made the important call — mechanics only, no art/lore. That handles most of the risk. Remaining housekeeping:
- Don't use the name "Ostranauts" or anything confusingly close; don't mimic its store-page/UI trade dress.
- Don't reference original assets/text/data/part-names as source material for your own or AI-generated content.
- Keep the asset-provenance register (source + license) for every model, texture, sound, and AI output.
- Do a quick name/capsule/UI sanity check before any public reveal.

Mechanics as abstract ideas (Newtonian flight, atmosphere sims, salvage loops, crew needs) are not protectable — you're free to build in this space. Risk only appears when the *expression* gets recognizably close, and your plan already avoids that.

---

## 10. Decisions locked & remaining open questions

**Locked:**
- Spiritual successor — mechanics only, no art/lore/names.
- Solo, single-player, Windows-first.
- C# on Godot 4.x, Forward+, Jolt.
- **Low-poly** art style (committed, §3).
- **First-person**, third-person optional later.
- **Locomotion:** grounded (mag-boot) walking prototyped first; all three modes (mag-boots / free-float / thruster EVA) long-term, context-chosen; precise maneuvering is the *earned* thing. Comfort options as settings.
- **Space model:** two-layer — strategic map (infinite, data) + tactical origin-local "bubbles" (bounded physics). Seamless-feeling travel with transitions.
- **Ship model:** three placement tiers, not voxels (full detail in Appendix A) — grid-snapped structural, free-position fixtures, floating loose objects; one shared flood-fill/graph solver for atmosphere + power + rooms; physical conduits with spark/fire hazard; full free building via a generalized verb system; optional wall covers that protect and insulate.
- **Atmosphere:** lumped per-volume scalars, no CFD (deliberate ceiling).
- **Time:** acceleration allowed when settled, disabled during active hazards/EVA; it's a presentation skip that still pays full costs.
- **Saves:** stable content IDs, version + migrations, missing-ID fallback, serialize live sim state.
- **Localization:** string tables from commit #1.
- **NPCs:** postponed well beyond MVP; only the crew-as-agents seam kept early.
- **Meta-structure: persistent survival campaign (Ostranauts model).** One continuous save; ship and gear persist and accrete; a character-creation lifepath (Traveller-style) produces your starting character + ship, then drops you into a populated area (station + derelicts). No roguelite runs, no meta-progression layer — "earning" upgrades is linear campaign progression over one save. Default pressure model: survival-against-entropy + economic pressure (O2, fuel, docking fees, repairs → salvage to stay solvent and alive), to be firmed up in Phase 2.
- **Complete-scope north star: fleet play.** The finished game has the player owning multiple ships with NPC crew operating vessels they aren't aboard (simulating in the background). Not MVP — but the architecture reserves room for it now via the §5 multi-ship seams. MVP ships one instance in a fleet of one.

**Intentionally undefined for now (and that's correct):**
Specific content — station identity, faction/economy detail, wreck variety, quest/event content, the depth of the lifepath. None of it blocks Phases 0–1, and defining it now would fight the "prove the loop first" discipline. It gets shaped during Phase 2 (systems depth) and Phase 4 (content). Wreck scale, procedural vs handcrafted, and ship-construction depth likewise fall out of the greybox rather than needing a decision today.

**Still open — a few narrow ship sub-decisions (Appendix A §A9):** conduit connectivity granularity (adjacent-cell vs. explicit ports), whether data/signal networks ship in MVP, and deck height in world units. None gate starting Phase 0. The big ship calls (three-tier placement, 1 m tiles, walls on shared edges) are settled.

**Nothing else blocking the start.** The moment-to-moment game, the overall structure, the space and ship models, and the core sim rules are all pinned. Phases 0–1 are ready to become a concrete Claude Code backlog whenever you want it.

---

## Appendix A — Ship architecture (detail)

The deepest structural decision in the project: how a ship is represented in data, because *everything* touches it — building, salvage, atmosphere, power, saving, procedural generation, and fleet play. Get it right once and reuse it everywhere; fork it and you pay forever. Grounded in how Ostranauts solves the same problems, adapted to 3D.

### A1. Why not a voxel grid

A uniform 1×1×1 m cube grid fails for the reasons that came up in design: walls/floors/ceilings *stack* within one floor-square rather than being separate cubes; switches and cables are far smaller than a tile; and shrinking cells to fixture scale (~10 cm) explodes cell count ~1000×, wrecking pathfinding, atmosphere flood-fill, and save size for zero benefit — players build at ~1 m resolution regardless.

### A2. The core idea — three placement tiers

Placement splits by *how it's positioned*, which matches "big stuff in tiles, small stuff wherever":

- **Tier 1 — Structural (grid/edge-snapped).** The airtight skeleton: floors and ceilings (per tile), walls (per shared *edge*), and large machinery footprints (per tile-group). Snapped, because the sim needs discrete boundaries — pathfinding, airtightness, and the flood-fill solver all read this tier. Also what you naturally place in tile units.
- **Tier 2 — Fixtures (surface-attached, free position).** Conduits, switches, lights, valves, wall/floor-mounted devices. Each attaches to a *surface* the structural tier exposes (floor-top, floor-underside, a wall's inner/outer face, ceiling-underside) and sits anywhere on it — free continuous position, not a slot. This is "cable under the floor, behind a wall, or exposed in front of it." Fixtures never affect airtightness; they feed the network graphs.
- **Tier 3 — Loose objects (free transform, physics).** Items, cargo, tools — anything not bolted down. Pure continuous position, physics-driven: rest on surfaces under gravity, **drift in zero-g** ("drop it and it floats"). No grid role.

There's no fixed slot list every object must fit into. The structural tier *creates surfaces*; fixtures *attach to surfaces at free positions*; loose objects *float free*. "Subfloor," "behind the wall," "on the ceiling" are just *which surface a fixture attached to*.

### A3. Walls on shared edges (one wall, never two)

An **edge** is a first-class entity: the boundary between two adjacent tiles (or a tile and the outside). Each tile has four edges, but an edge is *shared* with its neighbour. You build **one** wall on an edge and it's the boundary for both tiles — no `WallA <> WallB` double-building. In 3D the wall has thickness and two visible faces (inner/outer); fixtures attach to either face, which is how "behind" vs. "in front of" the wall works. Floors and ceilings are the horizontal equivalent — one shared boundary per tile, never doubled; deck N's ceiling plane is the same boundary as deck N+1's floor.

### A4. Machinery — footprint + mounts

A machine declares a **footprint** (1+ tiles) and optionally a **mount surface**. Big floor-standing machinery occupies its tile footprint and blocks pathfinding (walk around it); small devices mount to a wall/floor face as fixtures and don't block movement. Multi-tile machines are simply a footprint larger than one tile.

### A5. Decks and verticality

A ship is a vertical stack of tile grids (decks), sharing horizontal boundaries between levels. Deck-to-deck traversal is ladders/lifts under gravity, or floating through hatches in zero-g (handled by the movement system, not the ship model).

### A6. The one-solver trick (the big win)

Three systems reduce to the **same connectivity engine** reading the structural tier:

| System | "Network" is… | Flow blocked by… |
|---|---|---|
| **Atmosphere** | flood-fill of connected cells | sealed edges/floors/ceilings, closed doors |
| **Power** | connected component of conduits + attached machines | a gap in the cable run, or an open switch |
| **Data/signal** (later) | connected component of data conduits | same |
| **Rooms** | flood-fill of enclosed cells + required fixtures | sealed boundaries |

Write **one** graph/flood-fill solver and get atmosphere, power, data, and room-detection from it — four subsystems collapse to one. Recompute lazily on structural change (wall placed/removed, switch toggled, breach opened), not per frame. Airtightness reads Tier 1 only; networks read Tier 2 connectivity.

### A7. Conduits and the fire loop

Conduits are Tier-2 fixtures routing power from batteries/reactor to components. **Connectivity is grid-based** (a conduit in a cell connects to conduits in adjacent cells and machines in reach); the free visual routing surface is cosmetic and affects repair access. Conduits have a type/quality (cheap-temporary vs. durable-permanent) and a condition. **Powered + damaged + O2 present → spark/fire**; high O2 raises risk. This emergent hazard falls straight out of (power state) × (condition) × (local atmosphere) — no scripting. Redundant runs matter; switches are toggleable conduits that double as the "cut power before repairing" safety action.

**Wall covers (post-MVP, optional).** Two build flows coexist: **exposed** (wall → mount wires/pipes → done) is the always-valid, finished baseline; **covered** (wall → mount wires/pipes → install a cover over them → done) is an optional upgrade. The cover is a **real placeable Tier-2 fixture** — surface-attached like the wiring — whose special behavior is that it visually hides and *gates access to* the fixtures on that wall segment. It doesn't affect airtightness (the wall does). The wires/pipes are always real mounted objects; the cover is a real object over them.

Covered has to earn its extra cost with real bonuses, or players default to exposed. The bonuses:
- **Protection (primary):** covered wiring/piping takes less damage (micrometeor spall, impacts, wear), and fires behind a cover ignite less readily and stay contained — the semi-enclosed space limits oxygen, so a fire is smaller, slower, or self-smothers instead of spreading into the room. "Less damage" and "fewer/contained fires" are one category: the cover physically shields what's behind it.
- **Insulation:** covered walls bleed less heat, so climate control costs less power and rooms hold temperature longer during a power loss — ties into the survival/power loop.
- **Habitability/morale (later, once crew exist):** finished covered interiors read as livable rather than industrial, giving a comfort bonus.

This makes the choice genuinely two-sided rather than cosmetic — a preventive-vs-reactive identity:
- **Exposed** — cheap, fast, easy repair access, and you *see* a fault spark early. But it degrades faster and its fires spread freely in open atmosphere.
- **Covered** — costs a part and a build step, and faults are hidden (alarm/smoke before flame; you must find the panel). But it's protected, fire-contained, and better insulated.

Anti-tedium rule: covering is a deliberate optional build step, but **repair tasks auto-remove and replace the cover** so you never manually un-cover to fix (same principle as auto-hauling). MVP ships **exposed only** (cheapest, proven); covers are a Phase-2+ depth feature, additive later since a cover is just another fixture.

### A8. Building, rendering, serialization

- **Building** is verb-driven (install/uninstall/repair/dismantle/scrap/haul/toggle), placement creating a placeholder a task fulfils over time (which is what time-acceleration skips). Reach is proximity-limited (~2 tiles) or you EVA to it. Full free building, per the design target.
- **Rendering** is a pure function of model state — the low-poly kit is the visual vocabulary of structural states; a breach swaps to a damaged variant. This is what makes an off-ship vessel simulate with no meshes loaded.
- **Serialization:** per-tile structural contents + per-edge/boundary states (type + condition) + fixture list `(id, tile, surface, free-position, condition, state)` + loose-object transforms. Networks recomputed on load rather than trusted. Stable content IDs + missing-ID fallback. A save holds a *list* of ships (fleet), each tagged with strategic-map location and sim LOD state.

### A9. Open sub-decisions

Resolved: 1 m tiles; walls on shared edges (single); three-tier placement; free-position fixtures on structural surfaces. Remaining:
- **Conduit connectivity granularity** — adjacent-cell connectivity (simplest, recommended for MVP) vs. explicit port-to-port linking (tighter routing puzzles, later).
- **Data/signal networks** — MVP or later? Recommendation: later; same solver when wanted.
- **Deck height in world units** — feel/measurement decision, defer to greybox.
