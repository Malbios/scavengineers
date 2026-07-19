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

## Missions / contracts

**What:** A mission board or console offering discrete objectives with a payout, three variants
raised together: retrieve a specific item from a specific derelict, fly cargo from A to B, carry
a passenger from A to B.

**Why it's good:** Gives travel and salvage runs an externally-set goal instead of pure
self-directed scavenging; cargo/passenger delivery would reuse the existing travel-console
destination system. Overlaps with — but is more concrete than — the already-logged NPC/crew
"station contract board" idea below.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail. Likely needs some kind of objective/reward tracking that doesn't exist yet,
and cargo/passenger delivery specifically implies a cargo-hold/passenger-carrying mechanic that
doesn't exist either.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Active thermal management

**What:** Heaters/coolers/insulation panels as installable ship fixtures — a second resource
budget (heat generated vs. lost vs. capacity) running parallel to the existing power system.

**Why it's good:** Temperature currently only exists reactively (a breach goes cold, a fire gets
hot) — this gives the player active control over it instead of just reacting to it.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Radiation hazard + shielding

**What:** A third hazard type (distinct from breach/fire) in derelicts with reactor damage,
gated by a new suit resource (rad shielding tank/plating) reusing the existing O2/N2/filter/
battery suit-slot pattern, with an exposure timer.

**Why it's good:** Adds hazard variety beyond breach/fire using an already-proven suit-slot
mechanism, plus a genuine "how long do I stay" time-pressure decision.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Bulky-cargo hauling

**What:** A cart or magnetic sled fixture for moving large salvage that doesn't fit in a
backpack slot back to the ship, instead of everything needing to be pocket-sized.

**Why it's good:** Ties directly into the "carry capacity/priority is an actual choice" language
from the Phase 1 exit gate — lets salvage runs include genuinely large finds. Distinct from the
missions idea's "cargo A→B" above, which is a delivery contract, not the physical act of moving
something oversized.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Hydroponics / CO2-scrubber loop

**What:** An installable life-support fixture that converts CO2 back to O2 and trickles out a
small amount of food over time.

**Why it's good:** Gives the home ship an active self-sufficiency system tied into the existing
atmosphere solver, instead of every resource being externally sourced via vendor purchases only.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Zero-g debris fields

**What:** Loose floating clutter blocking a vented room's path that has to be manually pushed/
cleared rather than just walked around, reusing the shove-on-collision physics that already
exists for loose items (`ItemPushImpulse` in `Player.cs`).

**Why it's good:** Turns navigating a breached room into a small physical puzzle instead of just
a stat-drain hazard to walk through.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Breach repair method choice (foam patch vs. weld)

**What:** A quick foam patch (consumes a canister, temporary, can fail again under stress) vs. a
full weld (consumes spare parts, permanent) as two different ways to repair an existing breach.

**Why it's good:** Distinct from the hull-stress idea above — that's about *new* breaches
appearing over time from wear; this is about how you fix one that's already there, trading speed
for permanence.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Cold/vacuum-sensitive cargo

**What:** Some salvaged items degrade if carried through a vented or frozen room too long
unprotected.

**Why it's good:** Adds route-planning tension when hauling something delicate back through a
hazard, instead of every item being equally indifferent to the environment.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Randomized object values + economy scanner

**What:** Salvageable objects carry a randomized value (within reason, not wildly swingy), plus
a new PDA cartridge/scan mode that shows current economy info about nearby objects — effectively
an appraisal tool.

**Why it's good:** Turns "is this worth grabbing" into a real per-item judgment call rather than
every instance of an item being worth the same flat price; reuses the existing PDA
cartridge/scan-mode pattern (health scan, power scan) for the new mode. Related to — but
distinct from — the already-logged dynamic vendor economy idea, which is about prices drifting
from your own recent selling activity rather than per-object value variance.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Emergency O2 candle

**What:** A burnable consumable/installable chemical O2 generator, distinct from tanks — adds O2
to the room's atmosphere directly rather than the suit, submarine-style.

**Why it's good:** A genuine "we're almost out" backup option that doesn't require a suit/tank
already being in good shape.

**Why not now:** Flagged as "for later" when raised (2026-07-19) — not evaluated against current
dev state in detail.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Barter trading

**What:** The vendor accepts specific salvaged goods in exchange for specific goods, instead of
every transaction running through credits.

**Why it's good:** A different transaction model than price tuning alone — makes "what do I have
that they want" its own kind of decision.

**Why not now:** Flagged as "for later, maybe" when raised (2026-07-19) — noted with some
hesitation, not a firm commitment like the rest of this list.

**Revisit when:** Not yet determined — bring it up explicitly rather than re-offering it.

---

## Low-health warning

**What:** A pulsing red vignette (and maybe an audio cue) once Health drops under some threshold
(e.g. 25%) — reuses the existing `ColdOverlay`/`BurnOverlay`/`SmokeOverlay` full-screen-cue
pattern in `Player.cs`, just keyed off `HealthPercent` instead of temperature/smoke.

**Why it's good:** Every other player stat (O2, freezing, burning, smoke) already gets a
full-screen visual cue — Health is the one stat that currently ends the run (via the death
screen) with zero advance warning. Complements the death screen directly.

**Why not now:** Not rejected as premature like the rest of this list — just not the one picked
when it came up on 2026-07-19; "something else" was chosen for that session instead.

**Revisit when:** Any time — ready to build whenever it's picked up, no prerequisite.

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
