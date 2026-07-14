# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Big atmosphere redesign: a breach (or an airlock bridged into a leaking room) now vents its WHOLE connected-to-outside component uniformly and immediately, replacing the old per-cell diffusion delay - matches real depressurization (internal pressure equalizes at the speed of sound, far faster than air escapes through a hole, so there's no realistic "moment of grace" for a cell a few tiles from the hole). This is a deliberate reversal of the diffusion "moment of grace" feature from earlier this session - confirmed physically correct and requested directly. IMPORTANT PACING NOTE: VentRatePerSecond=5.0 now applies to an ENTIRE room/ship at once regardless of size, so any breached component reaches near-vacuum in roughly 1-2 seconds - a big, deliberate pacing swing from before (used to take minutes for a large ship). Checklist: (1) breach the Derelict's first room and open the airlock - confirm the Home Ship's corridor depressurizes as a rushing, whole-room event, not a slow trickle; (2) stand at a tile right next to the airlock and one several tiles further into the ship - confirm both drop together, not one lagging the other; (3) judge whether the ~1-2s full-ship pacing feels right, too fast, or still too slow, and report back - VentRatePerSecond may need a follow-up tuning pass based on actual feel; (4) confirm a normal airlock between two undamaged, sealed ships still just gently shares/tops up air as before (regression check on the non-leaking path); (5) confirm two sealed rooms joined by opening an interior (unbreached) door still mix gradually, not instantly (diffusion's remaining, narrower role).

& $godotExe --path $projectPath $scene
