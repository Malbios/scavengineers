# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: the travel console's destination map now shows which target you've currently selected - the selected icon tints gold (was indistinguishable from the others), and the selection label under the map now names the destination ("Destination selected: Derelict 1") instead of just a generic "Destination selected". Checklist: (1) open the travel console's map, click a destination icon - confirm that icon turns gold and the label below names it; (2) click a different destination - confirm the old one goes back to white and the new one turns gold, label updates to the new name; (3) the Travel button enables only once something's selected, same as before.

& $godotExe --path $projectPath $scene
