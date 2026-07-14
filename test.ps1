# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fixed: opening the airlock to a breached Derelict barely drained the Home Ship's corridor - it dropped fast for ~1s then plateaued around 8% and sat there indefinitely instead of continuing toward vacuum. Cause: AirlockBridge's earlier fix (pull both sides straight to Vacuum once either has its own path to outside) only ever touches the bridge's own 2 cells - Home's OWN AtmosphereSystem had no idea it was being drained, so its whole-ship life-support regen (0.2/s, applied to all ~74 cells) kept fighting the bridge's single-point drain (5.0/s, one cell) via dilution through Diffuse. AirlockBridge now also calls AtmosphereSystem.MarkExternallyVented on both bridged cells while leaking, which suppresses regen for that whole connected component. Checklist: (1) with the Derelict's first room breached, open the airlock and confirm the Home Ship's corridor O2 keeps dropping continuously toward 0% over time instead of stalling partway; (2) confirm the Derelict side still reads 0% throughout (unaffected, already correct); (3) confirm a normal airlock between two undamaged, sealed ships still just gently shares/tops up air as before (regression check on the non-leaking path).

& $godotExe --path $projectPath $scene
