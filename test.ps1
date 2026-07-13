# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Breach a wall/floor/ceiling on Home Ship or Derelict and wait a while (temperature drifts toward vacuum slower than pressure/O2) - confirm a blue frost overlay eventually appears and Health starts dropping noticeably faster than O2 depletion alone would explain. Separately, get an unpowered/damaged conduit to catch fire and stay in that room for a while - confirm a red heat overlay eventually appears with its own Health drain, on top of the existing smoke effects. Repair the breach / let the fire die out (or extinguish it) and confirm both overlays clear and the extra Health drain stops.

& $godotExe --path $projectPath $scene
