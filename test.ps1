# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fix (take 4): the Home Ship's own east corridor still read 0% O2 near a closed Derelict airlock after the size-normalized tie-break - confirmed via real in-game debug logging that ALL overlapping zones reported the identical margin (0.092), because every room-type zone shares the same vertical (floor-to-ceiling) span, and a player standing near the floor is equally far from every zone's own vertical center - so the Y axis was accidentally deciding the tie instead of which room's horizontal footprint you're actually in. The containment check now only compares the horizontal (X/Z) axes, never Y. Checklist: (1) travel to any Derelict, walk the full length of the Home Ship's own corridor toward its closed airlock - confirm O2 stays normal and no cold overlay the entire way, including standing still near the door; (2) open the door and cross into the Derelict - confirm its real vacuum/cold still applies once inside; (3) close the door, back away down the corridor - still normal air the whole way; (4) same check at the Station-side airlock/corridor for good measure.

& $godotExe --path $projectPath $scene
