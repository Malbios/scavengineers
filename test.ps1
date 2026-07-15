# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: a battery's real remaining charge now follows it everywhere, not just while installed. Taking a battery out of the drill/flashlight used to silently discard its charge (always showed "Battery: 1", and reinstalling any spare always refilled to 100%); now the ejected battery keeps its actual charge - in a hand/backpack slot tooltip ("Battery: 42%"), when dropped/aimed at in the world (crosshair label now reads e.g. "Battery (42%)"), and reinserting it restores that same real charge instead of refilling to full. A used-up battery is now a genuine consumable you have to replace (buy/salvage a fresh one) rather than an infinite free recharge. Also: hovering an empty backpack grid slot no longer shows a "Backpack Slot" tooltip label at all. Checklist: (1) drain the drill or flashlight's installed battery partway, eject it, hover it in a hand/backpack slot - confirm the tooltip shows the real reduced percentage, not 100%; (2) reinsert it - confirm the drill/flashlight bar shows that same reduced charge, not a refill; (3) drag that battery out into the world and aim at it - confirm the crosshair label shows the same percentage; (4) pick it back up and check its slot tooltip again - same percentage; (5) confirm a fresh battery (bought from the station shop, or found as loot) still reads 100%; (6) hover an empty backpack grid slot - confirm no tooltip appears, while empty hand/Back/drill-battery/flashlight-battery slots still show their own labels as before.

& $godotExe --path $projectPath $scene
