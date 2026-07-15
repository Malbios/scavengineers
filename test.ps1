# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: the boxy greybox figure standing next to the station's shop console (Station/ShopFigure) no longer bobs up and down - it now stands firmly planted and instead has a breathing/idle animation (BreathingIdle.cs): a subtle torso breathing pulse, a gentle independent arm sway on each side, and an occasional random head turn that eases toward a new look-angle every few seconds. Checklist: (1) travel to the station and walk toward the shop console - confirm the figure's feet stay fixed on the floor (no floating/bobbing); (2) watch for a few seconds - confirm the torso subtly pulses (breathing), the arms sway slightly and not in perfect sync with each other, and the head occasionally turns a little to a new angle at randomized intervals; (3) confirm it still doesn't block your path to the console and the shop console still opens normally on interact.

& $godotExe --path $projectPath $scene
