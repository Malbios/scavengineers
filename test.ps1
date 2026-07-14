# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fix: the Derelict's pre-placed loose items (scrap metal, wall panels, spare parts, power cell) were falling forever through the floor on a fresh game, because TravelConsoleVerbTarget.SetShipPresence decollides the whole not-currently-present ship (including its floor) at startup, and a recent pushable-items fix removed the accidental freeze-forever safety net that used to mask this. Loose PickupItem/ContainerPickupItem now implement IPhysicsPresenceAware so they freeze/unfreeze in lockstep with their ship's own collision toggle. Checklist: (1) start a FRESH game (docked at Station) - travel to the Derelict and confirm its pre-placed loose items are all still there, sitting normally (or floating, in the actually-breached room, once truly in vacuum) - not fallen through the floor or missing; (2) travel back to the Station, then back to the Derelict again - confirm the items still behave correctly after that round trip (tests the ongoing travel path, not just the initial-load path); (3) while at the Home Ship (Station docked), confirm its own loose items (e.g. the Crowbar) still push/fall normally under gravity as expected from the earlier pushable-items fix - regression check that this fix didn't re-freeze items that should be live.

& $godotExe --path $projectPath $scene
