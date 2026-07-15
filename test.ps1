# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fix (take 3): walking anywhere down the Home Ship's own east corridor toward a closed Derelict airlock (not just right at the door) still read 0% O2 + cold overlay - confirmed via debug logging that the docked Derelict's own ShipZoneRoom1 (a 10-unit-wide zone right next to its docking corridor) spans clear across the seam into the Home Ship's own corridor entirely, and the previous zone-tie-break fix used a raw world-unit containment margin that's biased toward whichever zone is physically BIGGER - so the small, correct Home Ship corridor zone was losing almost everywhere except right at the very edge. The margin is now normalized (a fraction of each zone's own size), so zone size no longer decides ties. Checklist: (1) travel to any Derelict, walk the full length of the Home Ship's own corridor toward its closed airlock (not just standing right at the door) - confirm O2 stays normal and no cold overlay the entire way; (2) open the door and cross into the Derelict - confirm its real vacuum/cold still applies once inside; (3) close the door, back away down the corridor - still normal air the whole way; (4) same check at the Station-side airlock/corridor for good measure.

& $godotExe --path $projectPath $scene
