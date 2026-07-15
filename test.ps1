# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: Derelict3's procedurally-generated layout now persists across save/load (F5/F9) instead of re-rolling every boot. Checklist: (1) travel to Derelict3, note its room layout/breach positions; (2) press F5 to save; (3) quit the game entirely and relaunch via this script; (4) travel to Derelict3 again -- it should look IDENTICAL to before (same rooms, same breach positions, same loot) since the seed was persisted; (5) as a contrast check, if you delete/rename the save file (or just don't press F5 before quitting) a fresh relaunch should roll a NEW random layout, same as before this change.

& $godotExe --path $projectPath $scene
