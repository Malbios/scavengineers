# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Tweak: Derelict4 and Derelict5 are now procedurally generated too (same as Derelict3), so all three of Derelict3/4/5 should each be a distinct random ship, while Derelict1/Derelict2 stay as their hand-authored catalog layouts. Full playtest checklist: (1) travel to each of Derelict3, Derelict4, Derelict5 in turn -- each should have its own distinct room count/shape, its own hull breach positions, and its own scattered loot (not copies of each other); (2) confirm loot is reachable (not stuck behind a wall or off the grid) and looks sensible; (3) if a derelict rolled a fire hazard, confirm the damaged conduit/generator prop is positioned correctly and the hazard behaves normally (ignites/spreads/repairs); (4) confirm every hull breach actually vents its room (no silent double-room vacuum); (5) press F5 to save, quit and relaunch via this script, then revisit Derelict3/4/5 -- all three should look IDENTICAL to before the relaunch since their seeds persisted; (6) as a contrast check, without saving first, a fresh relaunch should roll NEW random layouts for all three; (7) Derelict1/Derelict2 should look exactly as they always have (untouched by this feature).

& $godotExe --path $projectPath $scene
