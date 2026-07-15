# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: hovering the drill or flashlight's battery slot in the inventory panel now shows "Battery: <percent>%" reflecting actual remaining charge, instead of the previous always-"Battery: 1" (a leftover generic item-count tooltip that never meant anything for a battery). Checklist: (1) equip a battery in the drill and/or flashlight, open its window, hover the battery slot - confirm the tooltip reads e.g. "Battery: 100%" right after installing; (2) drain it partway (use the drill/turn the flashlight on and wait) and hover again - confirm the percentage has gone down accordingly; (3) confirm every other slot's tooltip (ordinary items, the Back slot) is unaffected.

& $godotExe --path $projectPath $scene
