# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Live-query tracking (previous commit) fixed the stale-reading bug, but standing right at an airlock door could still read the WRONG ship's air - because the corridor-widening fix from two commits ago left a wide (~2 unit) region physically inside BOTH ships' zones at once, and the query just returns whichever one IntersectPoint happens to find first there. That wide overlap was only ever needed to make the old signal-based tracking (now removed) more reliable; the live query doesn't need it, so it just caused wrong-zone ambiguity. Reverted CorridorZoneShape back to its original 2.2 size (a thin ~0.2-unit overlap, not the 4.0 it became). Stand right at an airlock door (both open and closed) from the home-ship side and confirm the O2 reading matches your own ship, not the far one, all the way up to the door itself.

& $godotExe --path $projectPath $scene
