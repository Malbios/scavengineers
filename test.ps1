# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Found a real bug (confirmed the sim itself was already correct via an end-to-end probe test): the corridor zone right at each airlock threshold only overlapped the far ship's own zone by ~0.2 units - thin enough that crossing it in a single physics tick (easy at normal walking speed, or if a frame hitches) could skip the BodyEntered event entirely, leaving the player's HUD reading permanently stuck on the OTHER ship's zone (which never recovers if that ship has no life support) even while standing back on your own ship. Widened the shared CorridorZoneShape (2.2 -> 4.0 in length) so all three airlock crossings (Derelict east, Station west) get a robust ~2-unit overlap margin instead. Walk back and forth through the Derelict airlock a few times (including running/sprinting through quickly) and confirm the O2 HUD reading always correctly reflects whichever ship you're actually standing in - it should never get stuck showing the derelict's permanent 0% while you're demonstrably back on the home ship.

& $godotExe --path $projectPath $scene
