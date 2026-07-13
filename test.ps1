# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Items are back and correctly frozen in normal rooms now. Added the missing piece: CharacterBody3D doesn't automatically push RigidBody3D objects on collision, so walking into a drifting item in zero-g never actually moved it. Go to a breached/vacuum room, confirm a loose item there is unfrozen/floating, then walk into it and confirm it actually gets shoved away rather than just blocking you like a wall. Confirm it still drifts a bit and settles (not an infinite ice-skating slide) thanks to the earlier damping. Confirm items in normal-gravity rooms still don't move when bumped (frozen, as intended).

& $godotExe --path $projectPath $scene
