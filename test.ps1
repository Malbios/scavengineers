# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # On Home Ship or Derelict, pry/scrap open an exterior wall (not floor/ceiling) with the power drill to create a wall breach, then stand near it in zero-g and confirm you now feel a real decompression pull toward that wall breach (previously only floor/ceiling holes pulled you). Confirm the room's O2 still visibly vents through the wall breach as before (that part already worked). Repair the wall and confirm the pull stops.

& $godotExe --path $projectPath $scene
