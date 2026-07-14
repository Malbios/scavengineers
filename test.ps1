# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Loose items (e.g. the spawn crowbar) used to be completely frozen/immovable under normal gravity, only becoming real physics bodies while floating in a breached (vacuum) room - PickupItem/ContainerPickupItem now unfreeze once, on their own first physics tick, regardless of room pressurization, so they're pushable at all times; gravity itself is unaffected (still handled separately by ShipAtmosphereZone's own Area3D override). Checklist: (1) walk up to the spawn crowbar under normal gravity and confirm it can be pushed/nudged by walking into it, not just picked up; (2) confirm it still rests naturally on the floor via normal gravity/friction rather than sliding around on its own; (3) confirm existing zero-g drifting behavior for loose items in a breached room is unchanged.

& $godotExe --path $projectPath $scene
