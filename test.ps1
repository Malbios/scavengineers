# Manual test runner for whatever the current change needs a human to check in Godot.
# Claude updates the $scene (and args, if needed) each time there's something to test --
# just run this script as-is; you don't need to know or type the launch command yourself.
#
# Usage: & ./test.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"
$scene = "res://Scenes/World.tscn" # Fixed a crash: crossing a live airlock into the Derelict threw KeyNotFoundException from AtmosphereSystem.VolumeAt and froze the player mid-transit. Cause: the player's per-position O2 read (ShipAtmosphereZone.TileAt, added for the diffusion redesign) can compute a tile just past whichever ship's own modeled corridor length (WestCorridorLength/EastCorridorLength) - the physical threshold mesh runs slightly further than the modeled Deck cells. ShipSim.VolumeAt now treats any cell outside its own ship's Deck as Vacuum instead of crashing. Checklist: (1) walk all the way through the open airlock from the Home Ship into the Derelict and back, at a normal walking pace, and confirm no freeze/crash and smooth movement the whole way; (2) repeat crossing quickly (sprint through) in case speed changes which tile you land on; (3) re-check the diffusion checklist from last time still holds: a breach's far side "holds its breath" before dropping, the open airlock still reads as immediately dangerous, and O2% varies by actual standing position within a room.

& $godotExe --path $projectPath $scene
