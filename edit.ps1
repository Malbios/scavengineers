# Launches the Godot editor for this project (no scene/quit args) so you can open
# scenes and adjust things manually.
#
# Usage: & ./editor.ps1

clear

$godotExe = "C:\Tools\Godot v4.7\Godot_v4.7-stable_mono_win64_console.exe"
$projectPath = "C:\dev\scavengineers"

& $godotExe --editor --path $projectPath
