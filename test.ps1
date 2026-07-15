# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # New: you can now drag an item out of the inventory panel and drop it into the visible 3D world to physically discard/place it - previously releasing a drag anywhere outside an existing slot/window just silently cancelled it. Works for both hand slots, every backpack grid slot, and the equipped Back slot itself (drops the whole backpack, contents intact); the drill/flashlight battery slots are NOT included this pass (dragging those out still does nothing, unchanged). The drop lands wherever your cursor is aiming in the world (via a fresh raycast), within about 3m reach - aiming at empty space/sky refuses the drop and the item stays put. Checklist: (1) open inventory (Tab), drag an item from a hand slot out into the visible world and release - confirm it disappears from the slot and a loose, pick-up-able item appears roughly where the cursor pointed; (2) same for a backpack-grid slot; (3) drag the equipped Back slot itself out into the world - confirm the backpack drops as a loose container, both when empty and when it still has contents, and the backpack window/grid closes correctly; (4) try dropping while aiming at empty space (e.g. through an open breach, or off a platform's edge) - confirm the item stays in inventory instead of vanishing; (5) confirm dragging the drill/flashlight battery slot out into empty space still does nothing; (6) confirm ordinary slot-to-slot dragging (hand<->hand, hand<->backpack, battery slots, Back<->hand) is completely unaffected.

& $godotExe --path $projectPath $scene
