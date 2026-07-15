# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fix (take 2): standing right at/against a CLOSED airlock door on the Home Ship side (after traveling to a Derelict) still misread 0% O2 + cold overlay after the last fix - the real cause was ShipSim.VolumeAt returning a blanket Vacuum for the one boundary tile just past the corridor's own modeled length (where a closed door's own thickness can push your position past the tile edge), not zone selection. It now reads the nearest modeled neighbor's real air instead. Checklist: (1) travel to any Derelict, walk up to and press right against the CLOSED airlock door on the Home Ship side - confirm O2 stays normal and the cyan cold overlay does NOT appear; (2) open the door and actually cross into the Derelict - confirm its real vacuum/cold still applies once you're actually inside/past the threshold, same as before; (3) close the door, back away - still normal air; (4) same check at the Station-side airlock (should already be fine, confirm no regression).

& $godotExe --path $projectPath $scene
