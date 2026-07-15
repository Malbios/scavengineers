# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: the station trade console now opens a Buy/Sell tab panel instead of cycling one Buy/Sell verb per item at a time. Interacting with it opens a "SHOP" panel with two tabs (Buy/Sell), each listing all 10 tradeable items at once, greyed out when not currently possible (unaffordable/no room for Buy, none owned for Sell). Clicking a row buys/sells 1 unit immediately and the panel stays open, refreshing live, rather than closing like the travel map does. Checklist: (1) interact with the trade console - confirm the shop panel opens, mouse is freed, movement/look/interact/hotbar are suppressed; (2) Buy tab lists all 10 items with prices, greyed out for anything unaffordable or (once your backpack/hands are full) with no room; (3) Sell tab lists all 10 items, greyed out for anything you don't currently hold; (4) buy something - confirm Credits drops immediately, the item appears in inventory, and the row's own state (and the Sell tab's matching row) updates without closing the panel; (5) sell something back - confirm Credits rises and the item count drops the same way; (6) Close button and Escape both close the panel; (7) scroll-wheel verb-cycling no longer applies to this console (a single interact opens the panel directly, like the travel console already does).

& $godotExe --path $projectPath $scene
