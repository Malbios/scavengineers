# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: a /cq quality pass fixed 9 findings (architecture, test gaps, forward-looking risk) - no gameplay-visible changes intended, this is a code-health pass. Checklist: (1) F to toggle the flashlight/debug flashlight still works both on and off, same as before; (2) board the Derelict - confirm its breached room(s) still start at vacuum immediately (not a slow drain), same as before; (3) an interior door on the Home Ship (or a fresh unpowered ship) still starts closed and opens/closes normally via crowbar-pry; (4) drag the drill/flashlight battery slot into the world - still drops a loose battery with its real charge, tool goes empty, same as before; (5) F5 save, F9 load - position/inventory/backpack/dropped-container state all still round-trip correctly.

& $godotExe --path $projectPath $scene
