# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: moved the travel map's "Select a destination" prompt label - now sits between the TRAVEL title bar and the map area itself, instead of below the map. Checklist: (1) open the travel console - confirm the layout top-to-bottom is: title bar, then the prompt label, then the map, then the Travel/Cancel buttons; (2) click a destination - confirm the label (now above the map) goes blank and the icon turns gold, same behavior as before, just relocated.

& $godotExe --path $projectPath $scene
