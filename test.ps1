# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Found the real bug: this project's Jolt physics backend can't detect an already-frozen RigidBody3D via Area3D at all (confirmed Godot/Jolt issue #103767), so the zone could never find a frozen item again to unfreeze it - it stayed rigid forever. Fixed by using FreezeMode.Kinematic instead of the default Static, which Jolt does detect correctly. Go to a breached/vacuum room and confirm a loose item there now actually unfreezes/floats, and walking into it shoves it away instead of blocking like a wall. Confirm it still drifts and settles rather than sliding forever. Confirm items in normal-gravity rooms still don't move when bumped (frozen, as intended) and that ordinary pickup/interaction still works normally on both frozen and unfrozen items.

& $godotExe --path $projectPath $scene
