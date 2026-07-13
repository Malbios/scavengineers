# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # The first drift fix (damping alone) wasn't enough - items were still going missing on the Derelict. Now they start completely frozen (immovable, exactly like the old static pickups) and only unfreeze once their room is confirmed to be in vacuum. Delete user://savegame.json for a truly fresh test, then travel straight to the Derelict and confirm ALL of its loot (WallPanel1/2/3, ScrapMetal/2/3/4/5, SpareParts/2, PowerCell1) is still exactly where it was authored. Also confirm items on the Home Ship (Crowbar) still behave normally. Then confirm the actual feature still works: stand in a breached/vacuum room and confirm a loose item there visibly unfreezes and drifts/floats (doesn't just stay rigidly frozen forever) - if it never unfreezes, that's a separate bug in the new logic to report back.

& $godotExe --path $projectPath $scene
