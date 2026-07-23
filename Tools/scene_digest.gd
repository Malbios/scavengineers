extends SceneTree

## Prints a canonical, diffable digest of an authored scene: every node's path, class, script,
## groups and stored property values, with resource-valued properties expanded inline so a mesh is
## compared by its actual size and material colour rather than by which SubResource id it happens
## to point at.
##
## Exists because nothing in the test suite loads Scenes/World.tscn — Scavengineers.NodeTests is a
## separate, scene-less Godot project and WorldSceneRegressionTests reads the file as *text* — so a
## scene refactor (extracting Station.tscn, moving destination groups under a BubbleRoot) had no way
## to prove it changed nothing. `godot --headless --quit` catches an unresolved resource but not a
## mesh silently pointing at the wrong SubResource of the right type, which is exactly the
## "locally plausible but globally wrong" failure docs/project-plan.md §6 warns about. Digest
## before, digest after, diff the two.
##
## Scenes are instantiated but deliberately never added to the tree, so no _Ready runs, no geometry
## generates and no game starts — this reads authored data, fully expanded, and nothing else.
##
## Usage:
##   godot --headless --path <project> --script res://Tools/scene_digest.gd -- res://Scenes/World.tscn

## How far to recurse into resource-valued properties. 2 reaches a MeshInstance3D's mesh and that
## mesh's own material; deeper is only next_pass chains, which this project doesn't use.
const RESOURCE_DEPTH := 2

## Stored on every node but describing tree bookkeeping rather than authored content, so they are
## pure diff noise.
const SKIPPED_PROPERTIES := ["owner", "script", "multiplayer", "process_thread_group"]


func _initialize() -> void:
	var scene_paths := OS.get_cmdline_user_args()
	if scene_paths.is_empty():
		printerr("usage: --script res://Tools/scene_digest.gd -- res://Scenes/World.tscn [...]")
		quit(1)
		return

	for scene_path in scene_paths:
		var packed := load(scene_path) as PackedScene
		if packed == null:
			printerr("could not load '%s' as a PackedScene" % scene_path)
			quit(1)
			return

		print("=== %s ===" % scene_path)
		var root := packed.instantiate(PackedScene.GEN_EDIT_STATE_DISABLED)
		_dump_node(root, root)
		root.free()

	quit(0)


func _dump_node(node: Node, root: Node) -> void:
	var line := "." if node == root else str(root.get_path_to(node))
	line += "  [%s]" % node.get_class()

	var script := node.get_script() as Script
	if script != null:
		line += " script=%s" % script.resource_path

	# Godot adds internal bookkeeping groups (leading "_") that vary with tree state, not authoring.
	var groups := PackedStringArray()
	for group in node.get_groups():
		if not str(group).begins_with("_"):
			groups.append(str(group))
	groups.sort()
	if not groups.is_empty():
		line += " groups=[%s]" % ", ".join(groups)

	print(line)

	for entry in node.get_property_list():
		if not _is_authored(entry):
			continue
		print("    %s = %s" % [entry.name, _format(node.get(entry.name), RESOURCE_DEPTH, root)])

	for child in node.get_children():
		_dump_node(child, root)


func _is_authored(entry: Dictionary) -> bool:
	if (int(entry.usage) & PROPERTY_USAGE_STORAGE) == 0:
		return false
	if (int(entry.usage) & PROPERTY_USAGE_INTERNAL) != 0:
		return false
	return not SKIPPED_PROPERTIES.has(str(entry.name))


func _format(value: Variant, depth: int, root: Node) -> String:
	# A resolved [Export] Node reference stringifies as <Class#38654705891>, and that id is a fresh
	# allocation on every run — pure diff noise. What actually matters is which node it points at.
	if value is Node:
		return "->%s" % root.get_path_to(value as Node)

	if value is Resource:
		var resource := value as Resource

		# A "::" path means a SubResource living inside the scene file — the case worth expanding,
		# since its id is exactly what a bad extraction gets wrong. A real file path identifies
		# itself, so print it and stop.
		if resource.resource_path != "" and not resource.resource_path.contains("::"):
			return "<%s %s>" % [resource.get_class(), resource.resource_path]
		if depth <= 0:
			return "<%s ...>" % resource.get_class()

		var fields := PackedStringArray()
		for entry in resource.get_property_list():
			if not _is_authored(entry):
				continue
			fields.append("%s=%s" % [entry.name, _format(resource.get(entry.name), depth - 1, root)])
		return "<%s %s>" % [resource.get_class(), ", ".join(fields)]

	# Godot prints 2.0 and 2 for the same float depending on provenance; pin the formatting so a
	# re-authored value doesn't read as a change.
	if typeof(value) == TYPE_FLOAT:
		return "%.6f" % value

	return str(value)
