# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Open inventory (Tab): equip row should only show LeftHand/RightHand/Back (no more inline battery slots). Right-click the held/backpacked power drill -> its own "Drill Battery" window opens; drag a battery in/out of it and confirm insert/eject still works, then right-click the drill again to close it. Same check for the flashlight. Right-click the worn backpack (Back slot) -> a "Backpack" window opens with its contents instead of auto-showing; unequip the backpack while its window is open and confirm the window auto-closes. Drag the Inventory/Drill/Flashlight/Backpack windows by their title bar and confirm they move and can't be dragged fully off-screen. Press Tab to close the inventory and confirm all three item windows close with it. F5 to save after moving a window, F9 to load, confirm it snaps back to the saved position. Delete/rename user://savegame.json (or start fresh) and confirm windows default to a normal on-screen position instead of erroring.

& $godotExe --path $projectPath $scene
