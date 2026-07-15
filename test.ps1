# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: the Derelict's atmosphere zones (per-room O2/zero-g) are now generated procedurally from ShipSim's own grid shape instead of hand-placed in the scene -- same geometry, different mechanism. Checklist: (1) travel to any Derelict, cross the airlock from the Home Ship both directions, confirming O2/temperature read correctly at every step, especially standing right at the closed door on either side (the exact bug fixed earlier this session); (2) walk between the Derelict's own rooms, confirming each room's O2/zero-g (near the two hull breaches) reads correctly and zero-g kicks in only in the breached room(s), not the whole ship.

& $godotExe --path $projectPath $scene
