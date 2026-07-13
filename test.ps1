# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Let O2 hit 0% and confirm real Health drain + death (reload last save, or a full O2/Health revival if no save exists yet); confirm the old Power stat/bar is completely gone from the HUD; confirm the flashlight now needs its own battery (drag one into its new equip slot, confirm it drains with use and the beam cuts out at 0 charge, confirm the debug flashlight still bypasses all of this); confirm letting Hunger/Thirst/Energy hit 0% visibly halves movement speed without killing you; confirm F5/F9 save-load round-trips Health and the flashlight battery correctly

& $godotExe --path $projectPath $scene
