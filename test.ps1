# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # The vent-rate fix had a side effect: it also made the airlock bridge's shared average drag the HOME ship's connected room down toward vacuum way too fast (a 5s transit cost a third of the room's air, 30s open fully drained it). Fixed by dropping AirlockBridge's own cross-airlock rate 10x (0.5 -> 0.05) - a narrow airlock doorway should restrict flow much more than an open room, decoupling "derelict recovers/vents fast" from "home ship drains fast too". Open the airlock, confirm the Derelict room still reads near-0% O2 quickly, then do a normal quick walk-through and confirm your OWN ship's connected room barely drops (stays near 20%). Then try leaving the airlock open a long time (30+ seconds) and confirm the home room DOES gradually drain if you leave it open that long - that part is intentional.

& $godotExe --path $projectPath $scene
