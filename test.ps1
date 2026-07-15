# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: derelict layouts are now data-driven (Data/Ships/layouts.json via ShipSim.LayoutId) instead of one hardcoded shape shared by every derelict. Checklist: (1) travel to Derelict1 -- confirm it looks/behaves exactly as before (3 rooms, 2 hull breaches, fire hazard in room 1, interior-door-style boundary at column 6 still there); (2) travel to Derelict2 -- confirm it's now genuinely different: only 2 rooms (not 3), a single hull breach in room 2 (near the far end, not the middle), fire hazard/damaged conduit still present and repairable in room 1; O2 reads correctly crossing its airlock and walking between its two rooms; (3) Derelict3/4/5 should still be identical to how Derelict1 used to look (unchanged, no LayoutId set).

& $godotExe --path $projectPath $scene
