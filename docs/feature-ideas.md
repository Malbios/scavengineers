# Feature Ideas Backlog

Ideas raised during "what's next" discussions that were judged good but don't fit the current
dev state yet. Not a roadmap, not a commitment — a memory aid so we stop re-litigating the same
idea from scratch, and a place to note *why* it was parked so the "why not now" doesn't have to
be re-derived later. Revisit an idea when its trigger actually holds, not just because it comes
up again.

Current dev state (see `CLAUDE.md`): Phase 1 greybox vertical slice, well underway, no formal
Phase 1 exit-gate check done yet.

## Format

Each entry:
- **What** — the idea in one or two sentences.
- **Why it's good** — the case for it.
- **Why not now** — the actual reason it was parked.
- **Revisit when** — the concrete trigger to bring it back up.

---

## Random travel encounters

**What:** During transit between derelicts (currently a pure time-skip), roll occasional events —
a micrometeorite clips a hull section, debris forces a thruster burn that costs extra N2, a
distress beacon reveals a new salvage site.

**Why it's good:** Gives the "settled" travel state actual stakes, reuses existing
travel-console/time-skip plumbing instead of adding a new subsystem.

**Why not now:** Too early in the current dev state.

**Revisit when:** Core travel loop is settled and past the Phase 1 exit-gate check.

## Injury / first-aid system

**What:** A wound stat separate from Health, driven by specific causes (burns, lacerations,
fractures) that only heals through an active first-aid verb (bandage/splint, consuming a
medkit), rather than Health's passive regen.

**Why it's good:** Gives hazards lasting consequence beyond "don't stand in the smoke"; slots
into the existing verb system the same way Repair/Maintain already does.

**Why not now:** Too early in the current dev state.

**Revisit when:** Existing survival stats (O2/hunger/thirst/health) feel tuned and validated.

## Purchasable ship component upgrades

**What:** Vendor sells upgraded drop-in replacements (higher-capacity battery, more efficient
thruster, faster recharge station) that swap into the existing install/uninstall verb slots.

**Why it's good:** Credits-driven progression loop, reuses the existing shop system without
needing fabrication at all.

**Why not now:** Too early in the current dev state.

**Revisit when:** The economy (buy/sell loop) has enough throughput that "what do I spend
credits on" is a real question.

## Dynamic vendor economy

**What:** Buy/sell prices drift with recent activity — dump scrap metal, the price craters; parts
you haven't sold stay high.

**Why it's good:** Turns "sell now or hold?" into a real decision, makes salvage runs feel less
like a fixed paycheck.

**Why not now:** Too early in the current dev state.

**Revisit when:** Same trigger as ship upgrades above — economy has enough throughput to matter.

## Signal/data network wiring

**What:** Sensor→alarm/door circuits reusing the same flood-fill/graph solver
atmosphere/power already run on (the architecture already anticipates this as a third network
type).

**Why it's good:** Reuses existing infrastructure instead of adding new code; unlocks
automation-flavored puzzles for later content to build on.

**Why not now:** Postponed (previously deferred, reconfirmed 2026-07-19).

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

## Fabrication / crafting station

**What:** A fabricator verb-target that consumes scrap/spare parts to produce items (N2 tanks,
batteries, wall panels, etc.).

**Why it's good:** Gives salvage a second purpose beyond selling to the vendor.

**Why not now:** Postponed (previously deferred, reconfirmed 2026-07-19).

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

## Hull stress / gradual structural integrity

**What:** A gradual structural-integrity stat per section (from repeated micrometeorite hits,
age, poor repairs) that risks a *new* breach over time, as distinct from the existing binary
breach hazard.

**Why it's good:** Makes "settled" ship-tending an active decision instead of a one-time fix.

**Why not now:** Postponed (previously deferred, reconfirmed 2026-07-19).

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

## NPC / crew mechanics

**What:** Hireable crewmate(s) with simple task-assignment AI, or a station contract board.

**Why it's good:** Turns "solo player doing everything" into resource/time-management decisions.

**Why not now:** "A much later topic" — furthest out of everything on this list.

**Revisit when:** Core single-player loop (salvage, survive, maintain, travel) is fully fleshed
out first.

## Tool progression

**What:** A cutting torch, a better multi-tool, or tiered drill upgrades (faster extraction,
works on tougher hull material) beyond the current single power drill.

**Why it's good:** Gives scrap-selling proceeds somewhere concrete to go even before a
fabricator or upgrade-vendor system exists.

**Why not now:** Not fully assessed — the one idea marked "only partially useful" rather than
outright good-but-early.

**Revisit when:** Worth a closer look sooner than the rest of this list, but not fleshed out yet.
