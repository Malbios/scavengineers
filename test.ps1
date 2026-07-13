# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Found why opening the airlock into a breached room gave it "a bit of oxygen" for a while: AirlockBridge's equalize rate and AtmosphereSystem's vent rate were equal (0.5/s each), so pulling toward a shared average and pulling toward vacuum settled into a tug-of-war holding several percent O2 the whole time the airlock stayed open. Vent rate bumped to 5.0/s (10x the bridge) so vent decisively wins. Open the airlock to the Derelict and confirm the breached room no longer noticeably fills with air even briefly - it should read as vacuum (items floating, HUD showing near-0% O2) essentially the whole time the airlock is open, not just after waiting.

& $godotExe --path $projectPath $scene
