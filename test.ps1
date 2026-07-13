# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Confirm ordinary pickups (crowbar, scrap metal, wall panels, power cell, spare parts on the Derelict) still sit normally on the floor and are still pick-up-able under normal gravity. Breach a room's floor/ceiling/wall and confirm any loose item inside actually starts drifting/floating once the room reads as vacuum - not just the player. Confirm walking into a resting or drifting item doesn't cause jitter or launch the player. Confirm installed machinery (travel console, bunk, ship battery, switches, doors) is completely unaffected - still fixed in place exactly as before. Scrap a wall/dismantle a damaged conduit to trigger an overflow drop and confirm the dropped item still lands at the right spot.

& $godotExe --path $projectPath $scene
