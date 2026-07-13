# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # The FreezeMode.Kinematic fix didn't work either - confirmed via the actual Godot/Jolt maintainer comment on issue #103767 that this is a project-setting/perf-exclusion thing, not something a freeze-mode switch reliably works around. Redesigned so each item queries physics space directly for its own zone every frame (ShipAtmosphereZone.UpdateFreezeState), instead of the zone trying to find already-frozen items via GetOverlappingBodies - sidesteps the Jolt monitoring exclusion entirely rather than working around it. Go to a breached/vacuum room (the Derelict) and confirm loose items there actually float/drift now, and walking into one shoves it away instead of blocking like a wall. Confirm it drifts and settles (damping) rather than sliding forever or drifting through the breach and vanishing. Confirm items in normal-gravity rooms (Home Ship) still don't move when bumped, and that ordinary pickup/interaction still works on both frozen and floating items.

& $godotExe --path $projectPath $scene
