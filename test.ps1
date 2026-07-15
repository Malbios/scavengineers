# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: you can now drag the drill/flashlight's battery slot directly out into the world, not just onto another slot first - the battery keeps its real remaining charge (same as ejecting it into a hand/backpack slot), and the tool loses its battery exactly like before. Checklist: (1) open the drill or flashlight window, drag its battery slot straight out into the visible world and release - confirm the tool's battery indicator goes empty and a loose battery pickup appears in the world; (2) aim at that dropped battery - confirm the crosshair label shows the real remaining charge percentage, not 100% if it was partially drained; (3) pick it back up and check its slot tooltip - same percentage; (4) confirm dragging the battery slot onto another slot (hand/backpack, or the other tool's battery slot) still works exactly as before, unaffected.

& $godotExe --path $projectPath $scene
