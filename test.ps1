# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: real navigation. The Home Ship's travel console no longer blindly toggles Station<->Derelict - interacting with it opens a spatial map showing the Station plus five Derelicts, you pick one and confirm, then the same timed-travel flow as before carries you there. All five Derelicts clone today's layout/hazards (no unique wrecks yet - that's separate content work). Only one physical "away mission" airlock exists on the Home Ship; its far side gets repointed at whichever Derelict you travel to, rather than five separate doors. The map's icon area now has a procedurally-drawn starfield background (opaque, no more ship geometry showing through) instead of flat black. Checklist: (1) interact with the travel console - confirm the map opens, mouse is freed, movement/look/interact/hotbar are suppressed, and all 6 destinations render at distinct positions with the current location's icon disabled/greyed, over a starfield (not flat black, not see-through to the 3D scene); (2) pick a Derelict, confirm Travel, confirm the map closes and the existing progress bar/timer plays out, then you arrive at that Derelict (visible/collidable, loose pickups live, everything else - other Derelicts + Station - hidden); (3) travel back to Station, then to a DIFFERENT Derelict than before - confirm the airlock correctly rebinds (walking through lands in the new wreck, and the door reads closed even if you left it open at the previous wreck); (4) Cancel and Escape both close the map with no travel; (5) F5 save at a couple of different locations, F9 load, confirm the correct location is restored each time.

& $godotExe --path $projectPath $scene
