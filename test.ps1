# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # The zone-widening fix wasn't enough - confirmed via extensive probing (sim recovery, real zone geometry, even loading the actual production room-split/interior-door config) that the underlying atmosphere sim was correct the whole time; the bug was entirely in how the player tracks "which zone am I in." It relied on Area3D's BodyEntered signal (a one-shot event that's easy to miss - a fast crossing, or Jolt's own detection quirks), caching a stale ship/room reading with no way to self-correct. Replaced this entirely: the player now queries physics space directly every physics frame (ShipAtmosphereZone.FindZoneAt) for whichever zone it's actually standing in, same pattern already proven for the loose-pickup freeze fix - no caching, no stale signal, no possible transition to miss. Walk between the Home Ship's rooms and the Derelict/Station repeatedly, including sprinting through airlocks, and confirm the O2 HUD always matches whichever room you're actually standing in - it should never show a stuck reading from a ship/room you've already left.

& $godotExe --path $projectPath $scene
