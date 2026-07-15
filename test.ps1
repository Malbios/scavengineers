# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: Derelict3 now gets a fully procedurally-generated layout (random room count/width, hull breaches, optional fire hazard, loot) instead of a fixed catalog entry -- rolled fresh every time the game boots (no save persistence yet, that's next). Checklist: (1) travel to Derelict3 -- confirm it has a sensible room layout (room 1 at the airlock end, 1-3 more rooms beyond it), at least one visible hull breach, and if there's a damaged/burning conduit it's in a real room, not floating outside the hull; (2) confirm loot (scrap metal/wall panels/spare parts/power cells) is scattered around and actually reachable, not embedded in a wall or floor; (3) O2 reads correctly crossing the airlock and walking between rooms; (4) quit and relaunch a couple of times -- Derelict3's layout should look DIFFERENT each time (no save exists yet, so every boot re-rolls); (5) Derelict1/2 should be completely unchanged, Derelict4/5 still identical clones of the original layout.

& $godotExe --path $projectPath $scene
