# Scene authoring and verification

Craft knowledge for editing `.tscn` files and reasoning about node startup order. Not a design
document — `docs/architecture/` holds the settled decisions; this holds the things that have
actually bitten, and how to not get bitten again.

## The verification gap, and the two tools that close it

**Nothing in the test suite loads `Scenes/World.tscn`.** `Scavengineers.NodeTests` is a separate,
scene-less Godot project that can't reach `res://Scenes/` at all, and `WorldSceneRegressionTests`
reads scene files as *text*. The only blanket check is:

```powershell
& $godot --headless --path . --quit
```

which catches an unresolved resource but **not** a mesh silently pointing at the wrong `SubResource`
of the right type, a node left on a scene default, or a destination that never instantiated. That is
exactly the "locally plausible but globally wrong" failure `docs/project-plan.md` §6 warns about, and
for a while it made scene refactors effectively unverifiable.

### `Tools/scene_digest.gd` — did this refactor change anything?

```powershell
& $godot --headless --path . --script res://Tools/scene_digest.gd -- res://Scenes/World.tscn
```

Instantiates a scene **without adding it to the tree**, so no `_Ready` runs and no game starts, then
prints every node's path, class, script, groups and stored properties. Resource-valued properties are
expanded inline, so a mesh is compared by its actual size and material rather than by which id it
points at — which is the whole point. Node references print as paths, because Godot reallocates
instance ids every run.

Deterministic: two runs of an unchanged scene diff to zero lines. **Digest before, digest after, diff
the two — around any `.tscn` surgery.**

Compare as a multiset (`Sort-Object` both sides, then diff), not with a plain `Compare-Object`, which
is a set diff and will hide a duplicated line. A plain positional diff is also worth a look, but a
moved block shows up there as large churn — the Station extraction showed 165 changed lines
positionally and 16 as a multiset. Only the 16 were real.

Worked example: extracting `Station.tscn` out of `World.tscn` changed **16 lines out of 31,277**, all
of them deliberate node renames. Moving Station 2's overrides into an inherited scene changed **0**.

### `Tools/world_smoke.gd` — did this actually build?

```powershell
& $godot --headless --path . --script res://Tools/world_smoke.gd
```

Runs the real world headless for ~20 frames and reports what `DestinationManager` built, then quits
non-zero on a problem. The digest deliberately runs nothing, so it *cannot* see a list assembled at
runtime — this is the counterpart. It checks the things that fail silently: a destination that never
instantiated, an override whose property name was wrong (so the node kept its scene default), a
contract giver whose console reference was never wired, or more than one destination present at once.

Both tools are headless and self-quitting. Never launch the editor or a windowed run to eyeball a
scene — `Player._Ready` calls `CaptureMouse()`, which takes the cursor.

## `.tscn` facts learned the hard way

- **Only structural attributes belong in a `[node ...]` header** — `name`, `type`, `parent`,
  `groups`, `node_paths`, `index`, `instance`, `unique_id`. Any *property* written there is silently
  accepted and ignored. This shipped once: `collision_layer` in the header left `Ceiling` and
  `FloorAimHelper` on the default layer, physically blocking movement (commit `bdcd15a`).
  `WorldSceneRegressionTests` guards it now.
- **`node_paths` must list exactly the NodePath exports the block sets** — no more, no less. Drop a
  property, drop its entry.
- **Overriding a child of an instanced scene needs `index="N"`**, N being that child's index among
  its parent's children *in the instanced scene*.
- **Inherited scenes** are how a variant carries non-scalar differences. The root is
  `[node name="X" instance=ExtResource("1")]` — no `type`, no `parent` — and children override with
  `parent="."`. `Scenes/Station2.tscn` inherits `Station.tscn` this way. Verified byte-identical by
  digest, so it's a safe pattern here.
- **`[Export] Godot.Collections.Array<Node3D>` has no proven `node_paths` auto-resolve.** Use
  `Array<NodePath>` plus `GetNode` in `_Ready`. (Better still, don't: prefer data + a registration
  call, which is what replaced the travel console's seven arrays.)
- **These files are LF, not CRLF.** A PowerShell `.Replace()` using `` `r`n `` silently matches
  nothing and reports success. Use `[System.IO.File]::ReadAllText` / `WriteAllText` with `` `n ``,
  and *assert the string actually changed* — a no-op replace looks exactly like a passing edit.
- **Orphaned `sub_resource` blocks are not an error.** Godot won't complain, and they accumulate.
  After a refactor, diff declared ids against `SubResource("...")` uses. Remove what *your* change
  orphaned; leave pre-existing orphans alone (there are 14 in `World.tscn`).

## Node startup order

This is where the subtle bugs live, and where a green test run proves the least.

- `_Ready` fires **depth-first, children before parents**, in sibling order.
- A node added with `AddChild` while the tree is already active readies **immediately**.
- The `CallDeferred` queue is FIFO, and calls queued *during* a flush go to the back of that same
  flush. `TravelConsoleVerbTarget` defers **twice** deliberately to exploit this: it readies before
  `DestinationManager` has instantiated anything, so the second defer lands its
  `ApplyCurrentLocation` behind every destination's own deferred `SeedDefaultShipLayout`. Without it,
  walls that don't exist yet can't be decollided, and every non-starting destination stays solid.
- **`SceneTree.PhysicsFrame` fires *before* any `_PhysicsProcess`.** A test that awaits a fixed
  number of frames and then asserts is racing, and it wins on an idle machine. Wait on a *condition*
  instead — `Scavengineers.NodeTests/FrameWait.cs`.
- To surface this class of bug at all: force a rebuild before each run
  (`(Get-Item <some .cs>).LastWriteTime = Get-Date`) and run 4–6 times. A few clean back-to-back runs
  prove nothing. This is not hypothetical — intermittent failures here were once misattributed to a
  code change that turned out to be innocent; running `HEAD` under forced rebuild reproduced them.

## When you add a saveable node

`SaveManager` captures state by scanning the **live tree** for the `saveable` group and keys purely
by `SaveId`. Two consequences worth internalising before editing a shared scene:

- **Duplicate ids don't error.** The second capture overwrites the first, and both nodes load the
  same state back. A `SaveId` authored in a shared scene is inherited by every instance that doesn't
  override it — this shipped as a live bug in `Derelict.tscn` (`Deck2/Floor2`), collapsing all five
  wrecks' second-deck build state onto one key.
- **Anything not in the tree is not saved.** See `architecture/save-schema.md`.

Destination ids now come from `Data/destinations.json` overrides, and
`WorldSceneRegressionTests.NoTwoDestinations_ShareASaveId_OnceTheirJsonOverridesAreApplied` resolves
every effective id — through JSON overrides *and* scene inheritance — and asserts uniqueness.
