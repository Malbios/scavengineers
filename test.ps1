# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Start a FRESH game (delete/rename any existing user://savegame.json first - a save from before this fix may already have scattered loot that this fix can't retroactively pull back). Travel from Home Ship to the Derelict right away (don't dawdle) and confirm all of its loot (WallPanel1/2/3, ScrapMetal/2/3/4/5, SpareParts/2, PowerCell1) is still sitting where it was authored, not missing/drifted away - this was the actual reported bug (items vanished by the time the player arrived, because Derelict starts fully vented/zero-g from world-load and an undamped physics nudge could drift forever). Separately confirm a bumped/pushed item in zero-g still drifts noticeably for a couple of seconds (a felt float) before settling, rather than snapping to a stop instantly.

& $godotExe --path $projectPath $scene
