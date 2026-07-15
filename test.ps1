# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: a boxy low-poly greybox figure (a decorative placeholder, not an NPC - no interaction, no dialogue) now stands next to the station's shop console (Station/ShopFigure), gently bobbing up and down (IdleSway.cs animates a visual-only child node, no collision movement). Checklist: (1) travel to the station and walk toward the shop console - confirm a boxy humanoid figure (teal-green suit-colored torso/legs/arms, skin-toned head box) stands nearby; (2) confirm it's gently bobbing in place; (3) confirm it doesn't block your path to the console and the shop console still opens normally on interact.

& $godotExe --path $projectPath $scene
