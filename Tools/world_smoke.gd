extends SceneTree

## Runs Scenes/World.tscn headless for a few frames and reports what DestinationManager actually
## built, then quits. Complements Tools/scene_digest.gd: the digest compares *authored* data without
## running anything, so it cannot see a destination list that is assembled at runtime. This can.
##
## Checks the things that fail silently rather than erroring — a destination that never instantiated,
## an override whose property name was wrong so the node kept its scene default (two destinations
## quietly sharing a SaveId), a contract giver whose console reference was never wired, or more than
## one destination present at once.
##
## Usage: godot --headless --path <project> --script res://Tools/world_smoke.gd

## Enough frames for _Ready plus the travel console's deliberately twice-deferred
## ApplyCurrentLocation (see TravelConsoleVerbTarget._Ready) to have settled.
const SETTLE_FRAMES := 20

var _frames := 0
var _failures := 0


func _initialize() -> void:
	root.add_child((load("res://Scenes/World.tscn") as PackedScene).instantiate())


func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < SETTLE_FRAMES:
		return false

	_report()
	quit(1 if _failures > 0 else 0)
	return true


func _report() -> void:
	var bubble := root.get_node_or_null("World/BubbleRoot")
	if bubble == null:
		_fail("World/BubbleRoot does not exist")
		return

	print("BubbleRoot has %d destinations:" % bubble.get_child_count())

	var present := []
	for destination in bubble.get_children():
		var ship_sim := destination.get_node_or_null("ShipSim")
		var save_id: String = ship_sim.get("SaveId") if ship_sim != null else "<no ShipSim>"
		var layout: String = ship_sim.get("LayoutId") if ship_sim != null else ""
		var procedural: bool = ship_sim.get("ProcedurallyGenerate") if ship_sim != null else false

		print("  %-12s visible=%-5s ShipSim.SaveId=%-24s LayoutId=%-16s procgen=%s"
			% [destination.name, destination.visible, save_id, layout, procedural])

		if ship_sim == null:
			_fail("%s has no ShipSim" % destination.name)
		elif save_id == "":
			_fail("%s left ShipSim.SaveId empty - its override did not apply" % destination.name)
		if destination.visible:
			present.append(String(destination.name))

	if bubble.get_child_count() == 0:
		_fail("no destinations were instantiated")

	# Exactly one destination is spatially present at a time; more would mean SetShipPresence never
	# ran, and every ship would be visible stacked on each other from outside.
	if present.size() != 1:
		_fail("expected exactly 1 present destination, found %d %s" % [present.size(), present])

	_check_unique_save_ids(bubble)
	_check_contract_givers(bubble)

	print("world smoke: %s" % ("FAILED (%d)" % _failures if _failures > 0 else "ok"))


func _check_unique_save_ids(bubble: Node) -> void:
	var owners := {}
	for node in bubble.find_children("*", "", true, false):
		var save_id = node.get("SaveId")
		if typeof(save_id) != TYPE_STRING or save_id == "":
			continue
		if owners.has(save_id):
			_fail("SaveId '%s' shared by %s and %s" % [save_id, owners[save_id], node.get_path()])
		owners[save_id] = node.get_path()


func _check_contract_givers(bubble: Node) -> void:
	var found := 0
	for node in bubble.find_children("*", "StaticBody3D", true, false):
		var script: Script = node.get_script()
		if script == null or not script.resource_path.ends_with("ContractGiverVerbTarget.cs"):
			continue
		found += 1
		if node.get("ConsoleRef") == null:
			_fail("%s has no ConsoleRef - its contract offers cannot resolve destinations" % node.get_path())

	if found == 0:
		_fail("no contract giver was found in any destination")


func _fail(message: String) -> void:
	_failures += 1
	printerr("  FAIL: %s" % message)
