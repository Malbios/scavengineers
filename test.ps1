# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Two more decompression-pull fixes. (1) Opening the Home Ship's airlock toward the Derelict while actually docked at the Station (so it vents to space, not a real connected ship) produced NO pull at all - ActiveBreachPositions() only ever surfaced Floor/Ceiling per-cell breaches and per-EDGE wall breaches, never the per-CELL Wall breach an airlock uses (AirlockDoorVerbTarget.SetBreached) - that whole category of breach was invisible to the pull, now fixed. (2) After patching Room 1's hole and opening the door to Room 2 (which still has its own breach on the far wall), you got pulled straight toward that breach THROUGH the dividing wall instead of through the doorway - the two rooms are legitimately atmosphere-connected once the door's open, but the breach is physically in a different room/zone, and a straight-line pull doesn't know that. Both Player.cs and the item pull now also require the breach to be in the SAME zone (room) you're actually standing in, not just "connected" via some other room's door. Checklist: (1) dock at the Station, then open the Home Ship's Derelict-side airlock - confirm you (and any nearby loose item) now feel a real pull toward it, matching the rushing-air venting you already see; (2) on the Derelict, patch Room 1, open the door to Room 2 (breach on its far wall), stand just inside Room 1 near the doorway - confirm you feel NO pull yet (the breach is still in the other room); (3) walk through into Room 2 itself and confirm you DO feel the pull there, toward the actual hole, not through a wall.

& $godotExe --path $projectPath $scene
