# Locomotion

Full detail: `docs/project-plan.md` §4 "Locomotion" and §7 Phase 0 Spike 1/3.

## Settled

- **Grounded (mag-boot) walking is built first**, in Phase 0/1, decoupled from the sim/build systems so the greybox loop can be validated without the hardest movement mode gating it.
- **First-person throughout.** Third-person is optional, later, never required.
- Complete game (post-MVP) has three context-chosen modes: mag-boots (gravity/on hull), free-float (zero-g interiors/derelicts), thruster EVA (open vacuum). What the player *earns* over the campaign is precise maneuvering, not raw movement.
- Free-float gets an isolated Phase-0 feel spike (throwaway prototype) to de-risk comfort before it's wired into the loop.
- Comfort options (vignette, auto-orient, reference-"up") ship as settings once float exists.
- Crew NPC pathfinding stays grounded (standard navmesh) — never promise free-float EVA pathfinding for NPCs.

## Before editing this subsystem

Movement code should stay decoupled from ship/sim state — it reads ship geometry (surfaces, gravity fields) but the sim (atmosphere/power/hazards) must not depend on which locomotion mode is active.
