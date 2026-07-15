# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: dropped the "Destination selected: ..." label text under the travel map - the gold icon highlight alone now indicates the pick, the label just shows the "Select a destination" prompt beforehand and goes blank once something's selected. Checklist: (1) open the travel console's map, click a destination - confirm the icon turns gold and the label below goes blank (no "Destination selected" text); (2) click a different destination - confirm the highlight moves and the label stays blank; (3) Travel button still enables only once something's selected.

& $godotExe --path $projectPath $scene
