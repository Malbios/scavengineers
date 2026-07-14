# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # The Home Ship's default layout now seeds real conduits (see ShipBuildTarget.DefaultConduitRoute) wiring Battery/Switch/RechargeStation/TravelConsole/InteriorDoor/StationAirlock/DerelictAirlock together from game start - a straight spine along row 2 plus 4 vertical spurs up to row 0 - so a fresh game no longer needs any manual wiring to get everything powered. Checklist: (1) start a brand-new game (no save) and, WITHOUT placing any conduits yourself, turn the switch on and confirm the Travel Console, Interior Door, Station Airlock, Derelict Airlock, and Recharge Station all show as powered; (2) flip the switch back off and confirm all of them lose power together (proving it's one real connected network); (3) visually check the new conduit run along row 2 and the 4 spurs looks like a sensible connected wire, not overlapping/floating oddly; (4) confirm the conduits are still normal player-removable structure (e.g. can be picked up/scrapped like any other placed conduit).

& $godotExe --path $projectPath $scene
