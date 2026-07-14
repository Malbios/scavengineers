# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fixed: an open airlock into a room with its OWN separate breach used to let that room settle at a stable, safe-feeling O2% (e.g. ~5%) forever, because the airlock only pushed fresh air into its own doorway cell while the real breach elsewhere in the room drained far more weakly (via diffusion, not direct vent) - a tug-of-war, not a drain. AirlockBridge now checks AtmosphereSystem.IsConnectedToOutside on both sides: if either side already has its own path to vacuum, the bridge pulls BOTH cells straight toward Vacuum instead of averaging them - so the source room (e.g. Home Ship, even with life support) now rapidly loses air too, matching a real breach venting anything feeding it. Checklist: (1) repair the Derelict's original breach, let it partially refill via a brief airlock opening, close the airlock, then remove a DIFFERENT wall to reopen a breach elsewhere in that same room; (2) reopen the airlock and confirm the whole connected system - Home Ship included - now visibly and rapidly loses air together, not settling at a stable partial reading; (3) confirm a normal two-way airlock open between two otherwise-sealed, undamaged rooms still just gently shares/tops up air as before (no unexpected venting when nothing is actually breached).

& $godotExe --path $projectPath $scene
