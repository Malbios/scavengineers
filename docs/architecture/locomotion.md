# Locomotion

Full detail: `docs/project-plan.md` §4 "Locomotion" and §7 Phase 0 Spike 1/3.

## Settled

- **Grounded (mag-boot) walking is built first**, in Phase 0/1, decoupled from the sim/build systems so the greybox loop can be validated without the hardest movement mode gating it.
- **First-person throughout.** Third-person is optional, later, never required.
- Complete game (post-MVP) has three context-chosen modes: mag-boots (gravity/on hull), free-float (zero-g interiors/derelicts), thruster EVA (open vacuum). What the player *earns* over the campaign is precise maneuvering, not raw movement.
- **Thruster EVA has been pulled forward** and is live: sustained directional thrust in zero-g, gated on wearing the EVA suit's torso piece with a charged N2 tank (see `Scripts/Player/Player.cs`'s zero-g branch and `Scripts/Player/SuitResources.cs`). There's no real open exterior space to fly through yet (travel between ships is still a console abstraction, `docs/architecture/space-and-travel.md`), so this is currently only exercised inside small bounded interior rooms — the mechanic is real, its intended payoff (actual open-vacuum flight) is still ahead.
- Free-float's isolated Phase-0 feel spike is **done** (`Scenes/FloatSpike.tscn` +
  `Scripts/Player/FloatPlayer.cs`, kept as a standalone scene) and free-float is now live in the
  real loop: `Player._PhysicsProcess` switches `MotionMode` to `Floating` whenever the current
  cell's O₂ reads at or below `ShipAtmosphereZone.ZeroGO2Threshold`, or the player is airborne
  with no floor below. `ShipAtmosphereZone` applies a matching real gravity override so loose
  pickups float too.
- **Ladder climbing** is a third movement state (`Player.BeginClimbing`, driven by
  `LadderVerbTarget`) for deck-to-deck traversal under gravity — it overrides the
  grounded/zero-g fork for as long as it's held, since a mid-climb player between two decks needs
  `Floating` either way.
- **Decompression pull** is the one hazard that reaches into movement: while a room is genuinely
  vented *and* graph-connected to outside, breaches within range in the same zone pull the player
  toward them, unless a worn torso with a charged N2 tank lets them thrust against it.
- Comfort options (vignette, auto-orient, reference-"up") ship as settings once float exists.
  **Not built yet** — and there is no settings menu to hang them on either (see
  `docs/dev-notes.md`).
- Crew NPC pathfinding stays grounded (standard navmesh) — never promise free-float EVA pathfinding for NPCs.

## Before editing this subsystem

Movement code should stay decoupled from ship/sim state — it reads ship geometry (surfaces, gravity fields) but the sim (atmosphere/power/hazards) must not depend on which locomotion mode is active.
