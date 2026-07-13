# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Design call (confirmed with the user, not a bug): once the airlock is open, ANY path to outer space should feel dangerous immediately - not just the room with the actual hole. Reverted the "slow bridge" idea; AirlockBridge's own rate now matches AtmosphereSystem's vent rate (5.0) exactly, so opening the airlock rapidly vents your OWN ship's connected room too, same speed as the breach itself. This is intentional now: even a quick walk-through will cost real air. Open the airlock to the Derelict and confirm BOTH sides drop to near-0% O2 within a few seconds - your ship's connected room should NOT stay safe just because you're quick about it. Close the airlock and confirm your ship's room recovers over time via life support (HasLifeSupport, ~15-20s) rather than staying stuck at 0%.

& $godotExe --path $projectPath $scene
