# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: the debug/testing flashlight (the always-lit magenta stipend item, infinite power, never held) can now be toggled off and on with F, same key as the real flashlight - previously its beam was forced on unconditionally with no way to turn it off. It still starts on at a fresh game (same as before) and still never drains any battery. Checklist: (1) at a fresh game, confirm the flashlight beam is on by default; (2) press F - confirm the beam turns off, even with empty hands; (3) press F again - confirm it turns back on; (4) pick up/hold the real "flashlight" item in a hand and confirm F still toggles correctly and independently drains its own battery as before (unaffected by this change).

& $godotExe --path $projectPath $scene
