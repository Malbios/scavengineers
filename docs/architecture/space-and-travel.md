# Space and travel

Full detail: `docs/project-plan.md` §5 "Space representation — two layers".

## Settled

- **Two-layer model**, chosen specifically to get "infinite, seamless" space without float-precision jitter:
  - **Strategic layer:** a map/graph of locations (stations, wreck fields, POIs) — pure data, no physics, arbitrarily large. Felt scale lives here.
  - **Tactical layer ("bubbles"):** bounded, origin-local physics scenes instantiated around wherever the player is — a transit bubble (player's ship at origin) or an encounter bubble (target + player's ship). Always re-centered near origin, always small (≤~10 km) so floats never jitter.
- This gives seamless-*feeling* travel with transitions — not literally-continuous cross-universe flight (deliberately avoids the Star Citizen streaming problem).
- **A bubble is the natural unit of "fully simulated."** Ships in the current bubble run full fidelity; everything else runs the coarse multi-ship LOD tick (see `multi-ship-fleet.md`). One concept, two payoffs.
- **MVP uses a simplified/abstracted travel step**, not full Newtonian ship piloting/docking — that's deferred (see plan §4 "Deferred to post-MVP phases").

- **Temporary greybox stand-in (`Scenes/World.tscn`):** the Home Ship and the one current
  Derelict are hand-placed sibling node groups at a fixed offset in a single scene,
  joined by a real airlock (`AirlockDoorVerbTarget`/`AirlockBridge`) instead of the old
  scene-swap travel console. This is **not** the eventual strategic-layer/tactical-bubble
  model — it's a fixed single-wreck placeholder for Phase 1. Once wrecks are randomly
  generated/multiple, the strategic layer needs to actually pick a wreck and instantiate
  a bubble at runtime rather than a hardcoded second ship baked into the main scene.
  Don't extend the "just add another fixed sibling group" pattern to a second wreck —
  that's exactly the retrofit this two-layer model exists to avoid.

## Before editing this subsystem

Any physics-bearing scene must stay within the ≤~10 km bubble bound and re-center near origin — don't let a "just this once" scene span the strategic layer's distances directly.
