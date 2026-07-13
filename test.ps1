# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Freeze/unfreeze now confirmed working (items float once a room reads 0% O2) - remaining complaint was a pushed item losing momentum almost instantly. Dropped ZeroGSettleDamp from 2f to 0.4f on both PickupItem and ContainerPickupItem so a shove glides for a couple of seconds instead of stopping dead. Go to the Derelict, wait for a room to fully vent, and shove a loose item - confirm it now drifts noticeably further/longer before settling, rather than stopping almost immediately. Confirm it still eventually settles rather than drifting forever. Confirm items in normal-gravity rooms (Home Ship) still don't move when bumped.

& $godotExe --path $projectPath $scene
