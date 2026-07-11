using System.Collections.Generic;
using System.Linq;
using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Turns a ship's whole Floor into a free-form build target — any tile or edge the player is
/// aiming at (fed in via <see cref="SetAimPoint"/>, computed from the interact ray's hit point)
/// can have a conduit (tile or wall) or a wall segment (edge) installed/removed. Reuses
/// PowerSystem's existing adjacency rule for conduits and Deck.SealEdge/UnsealEdge for walls —
/// the exact same primitive InteriorDoorVerbTarget already uses at its two fixed edges — so no
/// Scavengineers.Sim changes are needed, just deciding which edge/tile the player means.
/// </summary>
public partial class ShipBuildTarget : StaticBody3D, IVerbTarget, IBuildTargetSaveable
{
    // Public so Player can compare its filtered/affordable verb against these exact instances to
    // decide when the placement ghost should show, without duplicating verb ids as strings.
    public static readonly Verb InstallConduitVerb = new("install_conduit", "VERB_INSTALL_CONDUIT", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("scrap_metal", 1)],
    };

    public static readonly Verb InstallWallVerb = new("build_wall", "VERB_BUILD_WALL", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1)],
    };

    private static readonly Verb RemoveConduitVerb = new("remove_conduit", "VERB_REMOVE_CONDUIT", DurationSeconds: 0.2f);
    private static readonly Verb RemoveWallVerb = new("remove_wall", "VERB_REMOVE_WALL", DurationSeconds: 0.2f);

    // How close (in meters) the aim point needs to be to a tile boundary before it resolves to
    // that edge instead of the tile itself — half of this margin on each side of every boundary.
    private const float EdgeMargin = 0.25f;

    private const float WallCenterHeight = 1.5f;
    private const float FloorConduitHeight = 0.2f;

    // ConduitDropMesh's own authored length (see World.tscn) — BuildWallConduitVisual scales a
    // fresh instance of it to whatever the *actual* measured gap to the floor conduit turns out
    // to be, rather than assuming a fixed distance (an earlier version did that and got it
    // wrong — computing the real delta between the two anchor points is the only version that
    // can't silently drift out of sync with the actual geometry).
    private const float WallToFloorDropHeight = WallCenterHeight - FloorConduitHeight;
    private const float WallMountRoomOffset = 0.15f; // matches WallConduitTransform's own push

    // The 4 cardinal neighbor tiles a floor conduit checks for its connection-aware shape (see
    // BuildFloorConduitVisual) — AlongX says whether that direction's arm needs the 90-degree
    // rotation (ConduitArmMesh is authored long-axis Z, i.e. the north/south direction).
    private static readonly (Vector2I Offset, bool AlongX)[] CardinalDirections =
    [
        (new Vector2I(0, -1), false),
        (new Vector2I(0, 1), false),
        (new Vector2I(-1, 0), true),
        (new Vector2I(1, 0), true),
    ];

    private enum AimKind { None, Tile, Edge }

    private enum PendingAction { InstallConduit, RemoveConduit, BuildWall, RepairHullWall, RemoveWall }

    /// <summary>One tile can carry several conduits at once — one floor-mounted (WallNeighbor
    /// null) plus up to one per bordering wall (WallNeighbor = the cell across that specific
    /// edge). They're deliberately small boxes rather than one big one, so a busy junction tile
    /// can hold multiple distinct wire runs (e.g. a corner turn) without them blocking each
    /// other — Scavengineers.Sim doesn't care: same tile, or a neighbor exactly one tile away,
    /// both already connect via PowerSystem's adjacency rule regardless of mount surface.</summary>
    private readonly record struct ConduitSlot(Vector2I Tile, CellCoord? WallNeighbor)
    {
        public bool OnWall => WallNeighbor is not null;
    }

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>The ship's root Node3D (HomeShip/Derelict) — tile/edge coordinates are computed
    /// relative to this, matching ShipSim's own i=floor(x+3), j=floor(z+3) grid mapping.</summary>
    [Export]
    public Node3D? ShipRoot { get; set; }

    /// <summary>The lone-conduit default shape — shown for a floor conduit with no connected
    /// neighbors at all (see BuildFloorConduitVisual), and for the Tile-aim ghost preview.</summary>
    [Export]
    public Mesh? ConduitMesh { get; set; }

    /// <summary>One arm segment, reused once per connection to build a conduit's straight/corner/
    /// T/cross shape (floor conduits, see <see cref="BuildFloorConduitVisual"/>) or its along-
    /// wall/into-the-room stubs (wall conduits, see <see cref="BuildWallConduitVisual"/>) — thin
    /// and short enough that several fit on one tile without visually merging into a blob.
    /// Authored long-axis Z (north/south); rotated 90 degrees for east/west arms.</summary>
    [Export]
    public Mesh? ConduitArmMesh { get; set; }

    /// <summary>The vertical leg of a wall-to-floor connector (see
    /// <see cref="BuildWallConduitVisual"/>) — a fixed length matching
    /// <see cref="WallToFloorDropHeight"/>, since every tile's wall-mount-to-floor-height gap is
    /// identical.</summary>
    [Export]
    public Mesh? ConduitDropMesh { get; set; }

    /// <summary>Distinct shape from <see cref="ConduitMesh"/> — thin front-to-back rather than
    /// thin top-to-bottom, so it reads as mounted flush against a wall face instead of lying on
    /// the floor. Same <see cref="ConduitMaterial"/> either way. Shown only for a wall conduit
    /// with no connections at all (see <see cref="BuildWallConduitVisual"/>) — connected ones use
    /// <see cref="ConduitArmMesh"/> instead, same as floor conduits.</summary>
    [Export]
    public Mesh? WallConduitMesh { get; set; }

    [Export]
    public Material? ConduitMaterial { get; set; }

    [Export]
    public Mesh? WallSegmentMesh { get; set; }

    [Export]
    public Shape3D? WallSegmentShape { get; set; }

    [Export]
    public Material? WallMaterial { get; set; }

    [Export]
    public Material? GhostMaterial { get; set; }

    /// <summary>The grid column of the room-split boundary (i.e. the edge between
    /// column-1 and column) — excluded from wall targeting entirely, since
    /// InteriorDoorVerbTarget already owns the two doorway edges there and would desync if this
    /// generic tool could silently reseal/unseal them too. -1 disables the exclusion.</summary>
    [Export]
    public int ExcludedEdgeColumn { get; set; } = -1;

    [Export]
    public string SaveId { get; set; } = "";

    private readonly Dictionary<ConduitSlot, Node3D> _placedConduits = new();
    private readonly Dictionary<(CellCoord, CellCoord), (MeshInstance3D Mesh, CollisionShape3D Collision)> _placedWalls = new();

    private Timer? _cycleTimer;
    private MeshInstance3D? _ghost;
    private bool _cycling;
    private Verb? _previewVerb;

    private AimKind _aimKind;
    private Vector2I _aimedTile;
    private CellCoord _edgeA;
    private CellCoord _edgeB;

    private PendingAction _pendingAction;
    private ConduitSlot _pendingSlot;
    private CellCoord _pendingEdgeA;
    private CellCoord _pendingEdgeB;
    private PlayerInventory? _pendingInventory;

    public IReadOnlyList<Verb> AvailableVerbs => ResolveAvailableVerbs();

    // No name label: the floor isn't a discrete object like the Computer or a hull breach, it's
    // just terrain you can build on — the ghost box alone communicates what's about to happen.

    public float? CurrentVerbProgress =>
        _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime) : null;

    public override void _Ready()
    {
        _cycleTimer = new Timer { OneShot = true, WaitTime = InstallConduitVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

        _ghost = new MeshInstance3D { Mesh = ConduitMesh, Visible = false };
        _ghost.SetSurfaceOverrideMaterial(0, GhostMaterial);
        AddChild(_ghost);
    }

    /// <summary>Called by Player every time it re-reads this as the current verb target, so
    /// AvailableVerbs/ExecuteVerb always act on whichever tile/edge the interact ray currently
    /// hits. Also keeps the placement ghost tracking the aim point even while it's hidden.</summary>
    public void SetAimPoint(Vector3 worldPoint)
    {
        var local = (ShipRoot ?? GetParent<Node3D>()).ToLocal(worldPoint);
        var fx = local.X + 3;
        var fz = local.Z + 3;
        var i = Mathf.FloorToInt(fx);
        var j = Mathf.FloorToInt(fz);
        var withinX = fx - i;
        var withinZ = fz - j;
        var distX = Mathf.Min(withinX, 1 - withinX);
        var distZ = Mathf.Min(withinZ, 1 - withinZ);

        _aimedTile = new Vector2I(i, j);

        if (Mathf.Min(distX, distZ) < EdgeMargin)
        {
            _aimKind = AimKind.Edge;

            if (distX <= distZ)
            {
                var neighborI = withinX < 0.5f ? i - 1 : i + 1;
                _edgeA = new CellCoord(i, j);
                _edgeB = new CellCoord(neighborI, j);
            }
            else
            {
                var neighborJ = withinZ < 0.5f ? j - 1 : j + 1;
                _edgeA = new CellCoord(i, j);
                _edgeB = new CellCoord(i, neighborJ);
            }

            if (IsExcludedColumn(_edgeA, _edgeB))
            {
                _aimKind = AimKind.None;
            }
        }
        else
        {
            _aimKind = AimKind.Tile;
        }

        UpdateGhostTransform();
    }

    /// <summary>Which install verb (if any) is currently highlighted — Player calls this every
    /// frame once it knows the active, affordable verb here. Drives both the ghost's visibility
    /// and its shape, since "install a conduit" and "build a wall" need different previews at
    /// the same aim point (see <see cref="UpdateGhostTransform"/>).</summary>
    public void SetPreviewVerb(Verb? verb)
    {
        _previewVerb = verb;
        UpdateGhostTransform();
    }

    private bool IsExcludedColumn(CellCoord a, CellCoord b) =>
        ExcludedEdgeColumn >= 0 &&
        ((a.X == ExcludedEdgeColumn - 1 && b.X == ExcludedEdgeColumn) ||
         (b.X == ExcludedEdgeColumn - 1 && a.X == ExcludedEdgeColumn));

    private IReadOnlyList<Verb> ResolveAvailableVerbs()
    {
        if (ShipSimRef is null)
        {
            return [];
        }

        switch (_aimKind)
        {
            case AimKind.Tile:
                return [_placedConduits.ContainsKey(new ConduitSlot(_aimedTile, null)) ? RemoveConduitVerb : InstallConduitVerb];

            case AimKind.Edge when !ShipSimRef.Deck.Cells.Contains(_edgeB):
            {
                // Boundary edge — the wall action only ever repairs an existing breach, never
                // creates one. A wall-mounted conduit needs an actual hull wall to mount on, so
                // it's only offered while that hull is intact — a breach means open vacuum
                // there, not a wall, so mounting a "wire" in that gap would float mid-air.
                var breached = ShipSimRef.Deck.IsHullBreached(_edgeA);
                var verbs = new List<Verb>();
                if (breached)
                {
                    verbs.Add(InstallWallVerb);
                }

                if (EdgeConduitVerb(wallPresent: !breached) is { } boundaryConduitVerb)
                {
                    verbs.Add(boundaryConduitVerb);
                }

                return verbs;
            }

            case AimKind.Edge:
            {
                // Interior edge — same "needs a real wall" rule: an open (unbuilt) doorway-less
                // gap between rooms has nothing to mount a conduit on until a wall goes up.
                var sealed_ = ShipSimRef.Deck.IsEdgeSealed(_edgeA, _edgeB);
                var verbs = new List<Verb> { sealed_ ? RemoveWallVerb : InstallWallVerb };
                if (EdgeConduitVerb(wallPresent: sealed_) is { } interiorConduitVerb)
                {
                    verbs.Add(interiorConduitVerb);
                }

                return verbs;
            }

            default:
                return [];
        }
    }

    // Cycled alongside the wall verb via the same multi-verb scroll selection every other
    // multi-verb target already uses — aiming at an edge can mean "the wall itself" or "a
    // conduit mounted on it," two different objects sharing one aim point. Null when nothing
    // is already placed AND there's no wall to mount a new one on — removal of an already-
    // placed conduit is always offered regardless (e.g. if the wall behind it got breached
    // afterwards), only fresh installation requires a real wall to exist first.
    private Verb? EdgeConduitVerb(bool wallPresent)
    {
        var slot = new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB);
        if (_placedConduits.ContainsKey(slot))
        {
            return RemoveConduitVerb;
        }

        return wallPresent ? InstallConduitVerb : null;
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (_cycling)
        {
            return;
        }

        if (verb.Id == InstallConduitVerb.Id || verb.Id == RemoveConduitVerb.Id)
        {
            ExecuteConduitVerb(verb, inventory);
            return;
        }

        if (_aimKind == AimKind.Edge)
        {
            ExecuteWallVerb(verb, inventory);
        }
    }

    private void ExecuteConduitVerb(Verb verb, PlayerInventory inventory)
    {
        // Tile aim -> floor-mounted at the aimed tile. Edge aim -> wall-mounted at the near-side
        // tile (_edgeA), same slot EdgeConduitVerb already checked to decide Install vs Remove.
        var slot = _aimKind == AimKind.Edge
            ? new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB)
            : new ConduitSlot(_aimedTile, null);

        var alreadyPlaced = _placedConduits.ContainsKey(slot);
        if ((verb.Id == InstallConduitVerb.Id) == alreadyPlaced)
        {
            return; // Install only valid on an empty slot, Remove only on an already-wired one.
        }

        _pendingAction = verb.Id == InstallConduitVerb.Id ? PendingAction.InstallConduit : PendingAction.RemoveConduit;
        _pendingSlot = slot;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    private void ExecuteWallVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != InstallWallVerb.Id && verb.Id != RemoveWallVerb.Id)
        {
            return;
        }

        var isBoundary = !ShipSimRef!.Deck.Cells.Contains(_edgeB);
        if (isBoundary)
        {
            if (verb.Id != InstallWallVerb.Id || !ShipSimRef.Deck.IsHullBreached(_edgeA))
            {
                return;
            }

            _pendingAction = PendingAction.RepairHullWall;
        }
        else
        {
            var edgeSealed = ShipSimRef.Deck.IsEdgeSealed(_edgeA, _edgeB);
            if ((verb.Id == InstallWallVerb.Id) == edgeSealed)
            {
                return; // Build only valid on an open edge, Remove only on a sealed one.
            }

            _pendingAction = verb.Id == InstallWallVerb.Id ? PendingAction.BuildWall : PendingAction.RemoveWall;
        }

        _pendingEdgeA = _edgeA;
        _pendingEdgeB = _edgeB;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    public void CancelVerb()
    {
        if (!_cycling)
        {
            return;
        }

        _cycling = false;
        _pendingInventory = null;
        _cycleTimer!.Stop();
    }

    private void OnCycleComplete()
    {
        _cycling = false;
        var inventory = _pendingInventory;
        _pendingInventory = null;

        switch (_pendingAction)
        {
            case PendingAction.InstallConduit:
                InstallConduit(_pendingSlot);
                break;
            case PendingAction.RemoveConduit:
                RemoveConduit(_pendingSlot);
                inventory?.Add("scrap_metal", 1);
                break;
            case PendingAction.BuildWall:
                ShipSimRef!.Deck.SealEdge(_pendingEdgeA, _pendingEdgeB);
                SpawnWallSegment(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.RepairHullWall:
                ShipSimRef!.Deck.RepairHull(_pendingEdgeA);
                SpawnWallSegment(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.RemoveWall:
                ShipSimRef!.Deck.UnsealEdge(_pendingEdgeA, _pendingEdgeB);
                FreeWallSegment(_pendingEdgeA, _pendingEdgeB);
                inventory?.Add("wall_panel", 1);
                break;
        }
    }

    private void InstallConduit(ConduitSlot slot)
    {
        var surface = slot.OnWall ? FixtureSurface.WallInner : FixtureSurface.FloorUnderside;
        ShipSimRef?.Deck.AddFixture(new ConduitFixture(ConduitFixtureId(slot), new CellCoord(slot.Tile.X, slot.Tile.Y), surface));

        _placedConduits[slot] = slot.OnWall ? BuildWallConduitVisual(slot) : BuildFloorConduitVisual(slot.Tile);

        // A newly wired tile can turn a neighboring conduit's dead-end stub into a straight/
        // corner/T piece (or grow this one's own arms toward neighbors that already existed) —
        // refresh everyone whose shape could have just changed.
        RefreshFloorConduitVisualsAround(slot.Tile);
        RefreshWallConduitVisualsAround(slot);
    }

    private void RemoveConduit(ConduitSlot slot)
    {
        ShipSimRef?.Deck.RemoveFixture(ConduitFixtureId(slot));

        if (_placedConduits.Remove(slot, out var visual))
        {
            visual.QueueFree();
        }

        RefreshFloorConduitVisualsAround(slot.Tile);
        RefreshWallConduitVisualsAround(slot);
    }

    /// <summary>Builds a floor conduit's shape from whichever of its 4 cardinal neighbor tiles
    /// currently carry any fixture at all (another conduit, a machine, the battery, ...) — same
    /// adjacency PowerSystem itself uses to decide connectivity, so the shape never lies about
    /// what's actually wired. Zero neighbors falls back to the plain lone-conduit box; each
    /// connected direction gets its own short arm meeting the others at the tile center, so 2
    /// opposite directions read as a straight run, 2 adjacent as a corner, 3 as a T, 4 as a
    /// cross — all built from the same one arm mesh, no per-shape art needed.</summary>
    private Node3D BuildFloorConduitVisual(Vector2I tile)
    {
        var container = new Node3D();
        AddChild(container);
        container.Position = ToLocal(TileCenterWorld(tile));

        var connected = CardinalDirections.Where(d => HasFixtureAt(tile + d.Offset)).ToList();

        if (connected.Count == 0)
        {
            var lone = new MeshInstance3D { Mesh = ConduitMesh };
            lone.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            container.AddChild(lone);
            return container;
        }

        foreach (var (offset, alongX) in connected)
        {
            var arm = new MeshInstance3D { Mesh = ConduitArmMesh };
            arm.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            arm.Position = new Vector3(offset.X * 0.25f, 0, offset.Y * 0.25f);
            arm.RotationDegrees = alongX ? new Vector3(0, 90, 0) : Vector3.Zero;
            container.AddChild(arm);
        }

        return container;
    }

    private bool HasFixtureAt(Vector2I tile)
    {
        var cell = new CellCoord(tile.X, tile.Y);
        return ShipSimRef?.Deck.Fixtures.Any(f => f.Tile == cell) ?? false;
    }

    /// <summary>Rebuilds one tile's floor conduit shape from its current neighbors, if it has
    /// one placed at all — a no-op otherwise (nothing to redraw).</summary>
    private void RefreshFloorConduitVisual(Vector2I tile)
    {
        var slot = new ConduitSlot(tile, null);
        if (!_placedConduits.TryGetValue(slot, out var existing))
        {
            return;
        }

        existing.QueueFree();
        _placedConduits[slot] = BuildFloorConduitVisual(tile);
    }

    private void RefreshFloorConduitVisualsAround(Vector2I tile)
    {
        RefreshFloorConduitVisual(tile);
        foreach (var (offset, _) in CardinalDirections)
        {
            RefreshFloorConduitVisual(tile + offset);
        }
    }

    /// <summary>Same idea as <see cref="BuildFloorConduitVisual"/>, adapted to a wall mount's
    /// relationships: another conduit further along the *same* wall (an along-wall arm, reused
    /// ConduitArmMesh again), a floor conduit on the same tile (a real 2-segment connector — a
    /// horizontal reach out from the wall then a vertical drop to floor height, both fixed
    /// lengths since every tile is the same size), or a *different* wall's conduit on the same
    /// tile (a short inward stub only — actually reaching it would mean wrapping the room's
    /// corner, real extra geometry this pass still skips). Floor takes priority if both a floor
    /// and another wall share the tile. Nothing connected at all falls back to the plain lone
    /// wall-conduit box.</summary>
    private Node3D BuildWallConduitVisual(ConduitSlot slot)
    {
        var tile = slot.Tile;
        var wallNeighbor = slot.WallNeighbor!.Value;
        var edgeA = new CellCoord(tile.X, tile.Y);

        var container = new Node3D();
        AddChild(container);
        var (position, loneRotation) = WallConduitTransform(edgeA, wallNeighbor, tile);
        container.Position = position;

        var directionToWall = new Vector2I(wallNeighbor.X - tile.X, wallNeighbor.Y - tile.Y);
        var alongWallOffsets = directionToWall.Y != 0
            ? new[] { new Vector2I(-1, 0), new Vector2I(1, 0) }
            : new[] { new Vector2I(0, -1), new Vector2I(0, 1) };

        var hasArm = false;

        foreach (var offset in alongWallOffsets)
        {
            var neighborTile = tile + offset;
            var neighborWallNeighbor = new CellCoord(neighborTile.X + directionToWall.X, neighborTile.Y + directionToWall.Y);
            if (!_placedConduits.ContainsKey(new ConduitSlot(neighborTile, neighborWallNeighbor)))
            {
                continue;
            }

            hasArm = true;
            var arm = new MeshInstance3D { Mesh = ConduitArmMesh };
            arm.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            arm.Position = new Vector3(offset.X * 0.25f, 0, offset.Y * 0.25f);
            arm.RotationDegrees = offset.X != 0 ? new Vector3(0, 90, 0) : Vector3.Zero;
            container.AddChild(arm);
        }

        var hasFloorCompanion = _placedConduits.Keys.Any(other => other.Tile == tile && other.WallNeighbor is null);
        var hasOtherWallCompanion = _placedConduits.Keys.Any(other => other.Tile == tile && other.OnWall && other != slot);

        if (hasFloorCompanion)
        {
            // A real connector reaching the floor conduit's own tile-center hub — measured
            // directly against that hub's actual position rather than an assumed fixed offset,
            // so it can't end up floating short of (or past) where the floor conduit really is.
            hasArm = true;

            var floorHubLocal = ToLocal(TileCenterWorld(tile));
            var delta = floorHubLocal - container.Position;
            var horizontalDelta = new Vector3(delta.X, 0, delta.Z);
            var horizontalDistance = horizontalDelta.Length();

            if (horizontalDistance > 0.01f)
            {
                // ConduitArmMesh's own authored length is 0.5 — scale it to whatever the real
                // horizontal distance is instead of assuming it's always exactly one value.
                var reachOut = new MeshInstance3D { Mesh = ConduitArmMesh };
                reachOut.SetSurfaceOverrideMaterial(0, ConduitMaterial);
                reachOut.Scale = new Vector3(1, 1, horizontalDistance / 0.5f);
                reachOut.Position = horizontalDelta / 2f;
                reachOut.RotationDegrees = Mathf.Abs(delta.X) > Mathf.Abs(delta.Z) ? new Vector3(0, 90, 0) : Vector3.Zero;
                container.AddChild(reachOut);
            }

            var drop = new MeshInstance3D { Mesh = ConduitDropMesh };
            drop.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            drop.Scale = new Vector3(1, Mathf.Abs(delta.Y) / WallToFloorDropHeight, 1);
            drop.Position = new Vector3(horizontalDelta.X, delta.Y / 2f, horizontalDelta.Z);
            container.AddChild(drop);
        }
        else if (hasOtherWallCompanion)
        {
            hasArm = true;
            var intoRoom = new Vector2I(-directionToWall.X, -directionToWall.Y);
            const float stubReach = 0.2f;
            var stub = new MeshInstance3D { Mesh = ConduitArmMesh };
            stub.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            stub.Position = new Vector3(intoRoom.X * stubReach, 0, intoRoom.Y * stubReach);
            stub.RotationDegrees = intoRoom.X != 0 ? new Vector3(0, 90, 0) : Vector3.Zero;
            container.AddChild(stub);
        }

        if (!hasArm)
        {
            var lone = new MeshInstance3D { Mesh = WallConduitMesh };
            lone.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            lone.RotationDegrees = loneRotation;
            container.AddChild(lone);
        }

        return container;
    }

    private void RefreshWallConduitVisual(ConduitSlot slot)
    {
        if (!_placedConduits.TryGetValue(slot, out var existing))
        {
            return;
        }

        existing.QueueFree();
        _placedConduits[slot] = BuildWallConduitVisual(slot);
    }

    private void RefreshWallConduitVisualsAround(ConduitSlot slot)
    {
        if (slot.OnWall)
        {
            RefreshWallConduitVisual(slot);

            var direction = new Vector2I(slot.WallNeighbor!.Value.X - slot.Tile.X, slot.WallNeighbor.Value.Y - slot.Tile.Y);
            var alongWallOffsets = direction.Y != 0
                ? new[] { new Vector2I(-1, 0), new Vector2I(1, 0) }
                : new[] { new Vector2I(0, -1), new Vector2I(0, 1) };

            foreach (var offset in alongWallOffsets)
            {
                var neighborTile = slot.Tile + offset;
                var neighborWallNeighbor = new CellCoord(neighborTile.X + direction.X, neighborTile.Y + direction.Y);
                RefreshWallConduitVisual(new ConduitSlot(neighborTile, neighborWallNeighbor));
            }
        }

        // Any other wall conduit sharing this tile may have just gained or lost the same-tile
        // companion that drives its inward "connects into the room" stub — this change (whether
        // it's the wall conduit itself, another wall on the same tile, or a floor conduit here)
        // is exactly that companion.
        foreach (var other in _placedConduits.Keys.Where(s => s.OnWall && s.Tile == slot.Tile && s != slot).ToList())
        {
            RefreshWallConduitVisual(other);
        }
    }

    /// <summary>Same edge position/rotation a wall segment would use, nudged a few centimeters
    /// off the wall's centerline toward whichever tile the conduit belongs to — reads as
    /// mounted on that tile's wall face instead of embedded in the wall itself.</summary>
    private (Vector3 Position, Vector3 RotationDegrees) WallConduitTransform(CellCoord edgeA, CellCoord edgeB, Vector2I nearTile)
    {
        var (position, rotationDegrees) = EdgeTransform(edgeA, edgeB);

        var midX = (edgeA.X + edgeB.X) / 2f;
        var midY = (edgeA.Y + edgeB.Y) / 2f;
        var offset = new Vector3(
            Mathf.Sign(nearTile.X - midX) * WallMountRoomOffset,
            0,
            Mathf.Sign(nearTile.Y - midY) * WallMountRoomOffset);

        return (position + offset, rotationDegrees);
    }

    private void SpawnWallSegment(CellCoord a, CellCoord b)
    {
        var visual = new MeshInstance3D { Mesh = WallSegmentMesh };
        visual.SetSurfaceOverrideMaterial(0, WallMaterial);
        AddChild(visual);

        var collision = new CollisionShape3D { Shape = WallSegmentShape };
        AddChild(collision);

        var (position, rotationDegrees) = EdgeTransform(a, b);
        visual.Position = position;
        visual.RotationDegrees = rotationDegrees;
        collision.Position = position;
        collision.RotationDegrees = rotationDegrees;

        _placedWalls[Deck.Normalize(a, b)] = (visual, collision);
    }

    private void FreeWallSegment(CellCoord a, CellCoord b)
    {
        if (_placedWalls.Remove(Deck.Normalize(a, b), out var pair))
        {
            pair.Mesh.QueueFree();
            pair.Collision.QueueFree();
        }
    }

    /// <summary>Ghost shape/position depends on both where you're aiming AND which of the
    /// (possibly two, on an edge) install verbs is currently highlighted — "install a conduit"
    /// and "build a wall" are different objects at the same aim point, so previewing the wrong
    /// one's shape would be actively misleading, not just imprecise. Always shows the plain
    /// lone-conduit/wall-box shape rather than a live connection-aware preview — a rough "here's
    /// where" indicator, not a promise of the final shape.</summary>
    private void UpdateGhostTransform()
    {
        if (_previewVerb != InstallConduitVerb && _previewVerb != InstallWallVerb)
        {
            _ghost!.Visible = false;
            return;
        }

        switch (_aimKind)
        {
            case AimKind.Tile:
                _ghost!.Visible = true;
                _ghost.Mesh = ConduitMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileCenterWorld(_aimedTile));
                break;

            case AimKind.Edge when _previewVerb == InstallConduitVerb:
            {
                _ghost!.Visible = true;
                _ghost.Mesh = WallConduitMesh;
                var (position, rotationDegrees) = WallConduitTransform(_edgeA, _edgeB, new Vector2I(_edgeA.X, _edgeA.Y));
                _ghost.Position = position;
                _ghost.RotationDegrees = rotationDegrees;
                break;
            }

            case AimKind.Edge:
            {
                _ghost!.Visible = true;
                _ghost.Mesh = WallSegmentMesh;
                var (position, rotationDegrees) = EdgeTransform(_edgeA, _edgeB);
                _ghost.Position = position;
                _ghost.RotationDegrees = rotationDegrees;
                break;
            }

            default:
                _ghost!.Visible = false;
                break;
        }
    }

    /// <summary>Local (to this Floor body) position + rotation for a 1-tile wall segment on the
    /// given edge — works uniformly for interior edges and boundary edges (where the "far" cell
    /// is off-grid), since the midpoint math is the same either way.</summary>
    private (Vector3 Position, Vector3 RotationDegrees) EdgeTransform(CellCoord a, CellCoord b)
    {
        var midX = (a.X + b.X) / 2f;
        var midY = (a.Y + b.Y) / 2f;
        var shipLocal = new Vector3(midX - 3 + 0.5f, WallCenterHeight, midY - 3 + 0.5f);
        var world = (ShipRoot ?? GetParent<Node3D>()).ToGlobal(shipLocal);

        // WallSegmentMesh is authored running along X (separates cells differing in Y) — rotate
        // 90 degrees when the edge instead separates cells differing in X.
        var rotationDegrees = a.X != b.X ? new Vector3(0, 90, 0) : Vector3.Zero;

        return (ToLocal(world), rotationDegrees);
    }

    private Vector3 TileCenterWorld(Vector2I tile)
    {
        var shipLocal = new Vector3(tile.X - 3 + 0.5f, FloorConduitHeight, tile.Y - 3 + 0.5f);
        return (ShipRoot ?? GetParent<Node3D>()).ToGlobal(shipLocal);
    }

    private static string ConduitFixtureId(ConduitSlot slot) => slot.WallNeighbor is { } neighbor
        ? $"player_conduit_{slot.Tile.X}_{slot.Tile.Y}_wall_{neighbor.X}_{neighbor.Y}"
        : $"player_conduit_{slot.Tile.X}_{slot.Tile.Y}_floor";

    public BuildTargetSaveData CaptureBuildState()
    {
        var data = new BuildTargetSaveData();

        foreach (var slot in _placedConduits.Keys)
        {
            if (slot.WallNeighbor is { } neighbor)
            {
                data.WallConduits.Add(new WallConduitCoord(slot.Tile.X, slot.Tile.Y, neighbor.X, neighbor.Y));
            }
            else
            {
                data.Conduits.Add(new TileCoord(slot.Tile.X, slot.Tile.Y));
            }
        }

        foreach (var (a, b) in _placedWalls.Keys)
        {
            data.Walls.Add(new EdgeCoord(a.X, a.Y, b.X, b.Y));
        }

        return data;
    }

    /// <summary>Replays saved tiles/edges through the same helpers Install/BuildWall use —
    /// already inventory-free at this level (the verb/cost logic lives in ExecuteVerb, never
    /// called here), so restoring a save never re-charges scrap_metal/wall_panel.</summary>
    public void ApplyBuildState(BuildTargetSaveData state)
    {
        foreach (var tile in state.Conduits)
        {
            InstallConduit(new ConduitSlot(new Vector2I(tile.X, tile.Y), null));
        }

        foreach (var wallConduit in state.WallConduits)
        {
            var tile = new Vector2I(wallConduit.TileX, wallConduit.TileY);
            var neighbor = new CellCoord(wallConduit.NeighborX, wallConduit.NeighborY);
            InstallConduit(new ConduitSlot(tile, neighbor));
        }

        foreach (var edge in state.Walls)
        {
            var a = new CellCoord(edge.AX, edge.AY);
            var b = new CellCoord(edge.BX, edge.BY);

            if (ShipSimRef!.Deck.Cells.Contains(b))
            {
                ShipSimRef.Deck.SealEdge(a, b);
            }
            else
            {
                ShipSimRef.Deck.RepairHull(a);
            }

            SpawnWallSegment(a, b);
        }
    }
}
