# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Atmosphere redesigned to diffuse per-cell (distance/hop-count now matters) instead of instantly equalizing a whole connected room every tick - a breach at one end of a corridor no longer drops the far end just as fast as the near end. Vent now only applies directly to the breached cell(s); everything else only feels it via diffusion carrying the drop outward tick by tick. The player's own O2 HUD/zero-g mode also now reads its ACTUAL current tile (not a fixed per-room tile), so different spots in the same room can genuinely disagree during an active breach. Checklist: (1) breach one end of a multi-cell room (or use the Derelict's own breach) and confirm the far side visibly "holds its breath" for a beat before dropping, and the whole room still fully vents in reasonable time; (2) stand at an open airlock into the breached Derelict and confirm it still reads as immediately dangerous; (3) stand at different spots in the SAME large room during an active breach and confirm the O2% changes based on where you're actually standing, not one fixed room-wide value; (4) confirm loose pickup items in that room may lag a few seconds behind your own zero-g reading near a fresh breach (items keep obeying gravity briefly after you already read vacuum) - this is a known, accepted limitation, not a bug.

& $godotExe --path $projectPath $scene
