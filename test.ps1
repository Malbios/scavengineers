# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Open inventory (Tab), right-click the drill to open its battery window, drag its installed battery onto a SPECIFIC empty backpack slot that is NOT the first free one (e.g. drop it on slot 5 while slots 1-4 are empty) and confirm it lands exactly in that slot, not the first free one. Try dropping it onto an already-occupied slot and confirm nothing happens (battery stays installed) rather than it silently landing elsewhere. Repeat both checks for the flashlight's battery. Also confirm dragging a battery from a backpack slot back into the drill/flashlight window still works as before.

& $godotExe --path $projectPath $scene
