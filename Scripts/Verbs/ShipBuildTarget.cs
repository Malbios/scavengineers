using System.Collections.Generic;
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
/// can have a conduit (tile) or a wall segment (edge) installed/removed. Reuses PowerSystem's
/// existing adjacency rule for conduits and Deck.SealEdge/UnsealEdge for walls — the exact same
/// primitive InteriorDoorVerbTarget already uses at its two fixed edges — so no Scavengineers.Sim
/// changes are needed, just deciding which edge/tile the player means.
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

    private enum AimKind { None, Tile, Edge }

    private enum PendingAction { InstallConduit, RemoveConduit, BuildWall, RepairHullWall, RemoveWall }

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>The ship's root Node3D (HomeShip/Derelict) — tile/edge coordinates are computed
    /// relative to this, matching ShipSim's own i=floor(x+3), j=floor(z+3) grid mapping.</summary>
    [Export]
    public Node3D? ShipRoot { get; set; }

    [Export]
    public Mesh? ConduitMesh { get; set; }

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

    private readonly Dictionary<Vector2I, MeshInstance3D> _placedConduits = new();
    private readonly Dictionary<(CellCoord, CellCoord), (MeshInstance3D Mesh, CollisionShape3D Collision)> _placedWalls = new();

    private Timer? _cycleTimer;
    private MeshInstance3D? _ghost;
    private bool _cycling;

    private AimKind _aimKind;
    private Vector2I _aimedTile;
    private CellCoord _edgeA;
    private CellCoord _edgeB;

    private PendingAction _pendingAction;
    private Vector2I _pendingTile;
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

    /// <summary>Shows/hides the translucent preview at the current aim point — Player calls
    /// this once it knows whether Install is actually the active, affordable verb here.</summary>
    public void SetGhostVisible(bool visible) => _ghost!.Visible = visible;

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
                return [_placedConduits.ContainsKey(_aimedTile) ? RemoveConduitVerb : InstallConduitVerb];

            case AimKind.Edge when !ShipSimRef.Deck.Cells.Contains(_edgeB):
                // Boundary edge — only ever repairs an existing breach, never creates one.
                return ShipSimRef.Deck.IsHullBreached(_edgeA) ? [InstallWallVerb] : [];

            case AimKind.Edge:
                return [ShipSimRef.Deck.IsEdgeSealed(_edgeA, _edgeB) ? RemoveWallVerb : InstallWallVerb];

            default:
                return [];
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (_cycling)
        {
            return;
        }

        switch (_aimKind)
        {
            case AimKind.Tile:
                ExecuteConduitVerb(verb, inventory);
                break;
            case AimKind.Edge:
                ExecuteWallVerb(verb, inventory);
                break;
        }
    }

    private void ExecuteConduitVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != InstallConduitVerb.Id && verb.Id != RemoveConduitVerb.Id)
        {
            return;
        }

        var alreadyPlaced = _placedConduits.ContainsKey(_aimedTile);
        if ((verb.Id == InstallConduitVerb.Id) == alreadyPlaced)
        {
            return; // Install only valid on an empty tile, Remove only on an already-wired one.
        }

        _pendingAction = verb.Id == InstallConduitVerb.Id ? PendingAction.InstallConduit : PendingAction.RemoveConduit;
        _pendingTile = _aimedTile;
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
                InstallConduit(_pendingTile);
                break;
            case PendingAction.RemoveConduit:
                RemoveConduit(_pendingTile);
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

    private void InstallConduit(Vector2I tile)
    {
        ShipSimRef?.Deck.AddFixture(new ConduitFixture(ConduitFixtureId(tile), new CellCoord(tile.X, tile.Y), FixtureSurface.FloorUnderside));

        var visual = new MeshInstance3D { Mesh = ConduitMesh };
        visual.SetSurfaceOverrideMaterial(0, ConduitMaterial);
        AddChild(visual);
        visual.Position = ToLocal(TileCenterWorld(tile));
        _placedConduits[tile] = visual;
    }

    private void RemoveConduit(Vector2I tile)
    {
        ShipSimRef?.Deck.RemoveFixture(ConduitFixtureId(tile));

        if (_placedConduits.Remove(tile, out var visual))
        {
            visual.QueueFree();
        }
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

    private void UpdateGhostTransform()
    {
        switch (_aimKind)
        {
            case AimKind.Tile:
                _ghost!.Mesh = ConduitMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileCenterWorld(_aimedTile));
                break;
            case AimKind.Edge:
                _ghost!.Mesh = WallSegmentMesh;
                var (position, rotationDegrees) = EdgeTransform(_edgeA, _edgeB);
                _ghost.Position = position;
                _ghost.RotationDegrees = rotationDegrees;
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
        var shipLocal = new Vector3(tile.X - 3 + 0.5f, 0.2f, tile.Y - 3 + 0.5f);
        return (ShipRoot ?? GetParent<Node3D>()).ToGlobal(shipLocal);
    }

    private static string ConduitFixtureId(Vector2I tile) => $"player_conduit_{tile.X}_{tile.Y}";

    public BuildTargetSaveData CaptureBuildState()
    {
        var data = new BuildTargetSaveData();

        foreach (var tile in _placedConduits.Keys)
        {
            data.Conduits.Add(new TileCoord(tile.X, tile.Y));
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
            InstallConduit(new Vector2I(tile.X, tile.Y));
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
