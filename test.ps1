# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: save/load now preserves per-item state, closing the gap from last session - a partially-charged loose battery (in a hand/backpack slot, or inside a dropped backpack) used to always reset to 100% on save/load; now its real charge round-trips correctly (F5/F9 or actually quit and relaunch). Hand slots are also now saved by position, so which hand held what round-trips exactly too. A real old save from before this change already exists on disk and should still load fine (falling back to the old format, just without preserved battery charge, as expected). Checklist: (1) drain the drill or flashlight's battery partway, eject it into a hand or backpack slot, F5 to save, F9 to load - confirm the ejected battery's tooltip still shows the same reduced percentage after loading, not reset to 100%; (2) note which hand holds what before saving - confirm the same hand holds the same item after loading; (3) drop a backpack containing a partially-charged battery in the world, save, load - confirm the respawned backpack's battery still shows its real charge; (4) load the pre-existing old save (F9 right after launching, before doing anything else) - confirm it loads without error, with inventory/backpack/drill/flashlight state all intact (battery charge just won't have been preserved, since that save predates this feature - expected).

& $godotExe --path $projectPath $scene
