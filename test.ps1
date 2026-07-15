# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fix: standing right at a closed airlock door on the Home Ship side (after traveling to a Derelict) no longer misreads the docked Derelict's own vacuum/O2 - the two ships' corridor zones genuinely overlap by a small margin right at the docked threshold, and the zone-lookup now deterministically prefers whichever zone's shape contains you more centrally instead of an arbitrary first-match. Checklist: (1) travel to any Derelict, walk up to the closed airlock door on the Home Ship side and stand right at it - confirm O2 reads normally (not 0%) and you don't get pulled/sucked toward anything; (2) open the door and actually cross into the Derelict - confirm its real vacuum still applies once you're actually inside/past the threshold, same as before; (3) close the door, back away into the Home Ship proper - still normal air, as always; (4) do the same check at the Station-side airlock for good measure (should already have been fine, but confirm no regression).

& $godotExe --path $projectPath $scene
