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
/// Turns a ship's whole Floor into a free-form build target — any tile, ceiling point, or edge
/// the player is aiming at (fed in via <see cref="SetAimPoint"/>/<see cref="SetCeilingAimPoint"/>,
/// computed from the interact ray's hit point) can have a conduit (tile or wall), a floor panel
/// (tile), a ceiling panel, or a wall segment (edge) installed/removed. Reuses PowerSystem's
/// existing adjacency rule for conduits, Deck.SealEdge/UnsealEdge for interior walls, and
/// Deck.BreachHull/RepairHull (now reason-tagged, see StructuralSurface) for both boundary walls
/// and floor/ceiling — so no new Scavengineers.Sim concepts are needed, just deciding which
/// surface the player means and which existing primitive represents it.
/// </summary>
public partial class ShipBuildTarget : StaticBody3D, IVerbTarget, IBuildTargetSaveable
{
    // Public so Player can compare its filtered/affordable verb against these exact instances to
    // decide when the placement ghost should show, without duplicating verb ids as strings.
    public static readonly Verb InstallConduitVerb = new("install_conduit", "VERB_INSTALL_CONDUIT", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("scrap_metal", 1)],
    };

    // Wall/floor/ceiling work (not conduits) needs a power drill in hand alongside the
    // consumed material — a real tool, not spent, gated on its own charge (see
    // Player.IsAffordable/Interact's drill-specific clauses).
    private static readonly ItemRequirement PowerDrillRequirement = new("power_drill", 1) { Consumed = false };

    public static readonly Verb InstallWallVerb = new("build_wall", "VERB_BUILD_WALL", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveConduitVerb = new("remove_conduit", "VERB_REMOVE_CONDUIT", DurationSeconds: 0.2f) { IsDestructive = true };

    private static readonly Verb RemoveWallVerb = new("remove_wall", "VERB_REMOVE_WALL", DurationSeconds: 0.2f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    // Floor and ceiling panels reuse the same wall_panel construction-part item as walls —
    // one item covers all three rather than inventing two more catalog entries for the same
    // purpose.
    private static readonly Verb InstallFloorVerb = new("install_floor", "VERB_INSTALL_FLOOR", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveFloorVerb = new("remove_floor", "VERB_REMOVE_FLOOR", DurationSeconds: 0.2f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    private static readonly Verb InstallCeilingVerb = new("install_ceiling", "VERB_INSTALL_CEILING", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveCeilingVerb = new("remove_ceiling", "VERB_REMOVE_CEILING", DurationSeconds: 0.2f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    // Same cost shape as InstallFloorVerb — it's the same construction work, just claiming a
    // brand-new cell instead of repairing an existing one's panel. Only offered alongside
    // InstallWallVerb on a boundary edge that's currently open (see ResolveAvailableVerbs) — you
    // extend through a gap you've made, not through an intact wall.
    private static readonly Verb ExtendFloorVerb = new("extend_floor", "VERB_EXTEND_FLOOR", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    // Battery/Switch/RechargeStation verbs — Install requires holding the machine's own item
    // (bought from a trade console, or refunded by a prior Uninstall); Uninstall gives that same
    // item back, Scrap gives partial scrap_metal instead (a real tradeoff, same shape as
    // DamagedConduitVerbTarget's Repair-vs-Scrap).
    private static readonly Verb InstallBatteryVerb = new("install_battery", "VERB_INSTALL_BATTERY", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("battery", 1)],
    };

    private static readonly Verb UninstallBatteryVerb = new("uninstall_battery", "VERB_UNINSTALL_BATTERY", DurationSeconds: 0.2f) { IsDestructive = true };
    private static readonly Verb ScrapBatteryVerb = new("scrap_battery", "VERB_SCRAP_BATTERY", DurationSeconds: 0.2f) { IsDestructive = true };

    private static readonly Verb InstallSwitchVerb = new("install_switch", "VERB_INSTALL_SWITCH", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("switch", 1)],
    };

    private static readonly Verb UninstallSwitchVerb = new("uninstall_switch", "VERB_UNINSTALL_SWITCH", DurationSeconds: 0.2f) { IsDestructive = true };
    private static readonly Verb ScrapSwitchVerb = new("scrap_switch", "VERB_SCRAP_SWITCH", DurationSeconds: 0.2f) { IsDestructive = true };

    private static readonly Verb InstallRechargeStationVerb = new("install_recharge_station", "VERB_INSTALL_RECHARGE_STATION", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("recharge_station", 1)],
    };

    private static readonly Verb UninstallRechargeStationVerb = new("uninstall_recharge_station", "VERB_UNINSTALL_RECHARGE_STATION", DurationSeconds: 0.2f) { IsDestructive = true };
    private static readonly Verb ScrapRechargeStationVerb = new("scrap_recharge_station", "VERB_SCRAP_RECHARGE_STATION", DurationSeconds: 0.2f) { IsDestructive = true };

    // How close (in meters) the aim point needs to be to a tile boundary before it resolves to
    // that edge instead of the tile itself — half of this margin on each side of every boundary.
    private const float EdgeMargin = 0.25f;

    private const float WallCenterHeight = 1.0f;

    // Half the conduit mesh's own 0.05 thickness above the floor's actual top surface (Y=0,
    // matching FloorPanelHeight's own surface-alignment note below) — resting flush on the
    // floor instead of the old 0.2 (visibly floating ~17.5cm above it).
    private const float FloorConduitHeight = 0.025f;

    // A wall face gets one conduit slot per tile-height's worth of its own height, stacked
    // vertically — a taller/shorter wall (if WallHeight ever changes) gets more/fewer slots
    // automatically rather than a hand-picked fixed count. WallHeight matches
    // WallSegmentShape/WallSegmentMesh's authored Y size (see World.tscn), the same
    // hand-kept-in-sync convention WallCenterHeight already uses for that same mesh.
    private const float WallHeight = 2f;
    private const float TileSize = 1f;
    private static readonly int WallSlotCount = Mathf.RoundToInt(WallHeight / TileSize);
    private static readonly float WallSlotHeight = WallHeight / WallSlotCount;

    // Match the existing (unsplit, collision-only) FloorShape/CeilingShape colliders' actual
    // top/bottom surfaces exactly, so the panel mesh sits flush with where the player's feet and
    // the ceiling's underside really are, instead of at the conduit's own mount height
    // (FloorConduitHeight) — sharing that height with conduits is what caused the panels to
    // z-fight with them.
    private const float FloorPanelHeight = -0.025f;
    private const float CeilingPanelHeight = 2.025f;

    // ConduitDropMesh's own authored length (see World.tscn) — BuildWallConduitVisual scales a
    // fresh instance of it to whatever the *actual* measured gap to the floor conduit turns out
    // to be, rather than assuming a fixed distance (an earlier version did that and got it
    // wrong — computing the real delta between the two anchor points is the only version that
    // can't silently drift out of sync with the actual geometry).
    private const float WallToFloorDropHeight = WallCenterHeight - FloorConduitHeight;
    private const float WallMountRoomOffset = 0.15f; // matches WallConduitTransform's own push

    // Each machine's own mount height/room-offset — matched exactly to the old hand-placed
    // World.tscn transforms (derived against each edge's own wall-boundary position) so the
    // retrofit doesn't shift anything. Recharge Station sits further into the room since it's a
    // station you approach, not a flush wall fitting like the other two.
    private const float BatteryHeight = 1f;
    private const float BatteryRoomOffset = 0.1f;
    private const float SwitchHeight = 1f;
    private const float SwitchRoomOffset = 0.1f;
    private const float RechargeStationHeight = 0.5f;
    private const float RechargeStationRoomOffset = 0.3f;

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

    // The doorway rows every ship's boundary/interior walls leave open for airlocks and interior
    // doors — matches ShipSim's own DoorwayRows exactly (kept in sync by hand; both are the same
    // "fixed-shape stand-in" the project plan already accepts pre-data-driven ship layouts, see
    // ShipSim.cs's own hardcoded grid).
    private static readonly int[] DoorwayRows = [2, 3];

    // The Home Ship's default Battery/Switch/RechargeStation edges — matches the old hand-placed
    // World.tscn positions exactly (see BatteryHeight/etc above). Battery and Switch sit on
    // Manhattan-adjacent tiles (mounted right next to each other on the same wall) — PowerSystem
    // already treats directly-adjacent fixtures as touching, no conduit segment needed between
    // them, same rule the Derelict's fire hazard relies on for its own adjacent pair.
    private static readonly (CellCoord A, CellCoord B) BatteryEdge = (new CellCoord(4, 0), new CellCoord(4, -1));
    private static readonly (CellCoord A, CellCoord B) SwitchEdge = (new CellCoord(5, 0), new CellCoord(5, -1));
    private static readonly (CellCoord A, CellCoord B) RechargeStationEdge = (new CellCoord(9, 0), new CellCoord(9, -1));

    // Default wiring for the Home Ship's seeded layout (see SeedDefaultShipLayout) — a straight
    // utility spine along the row-2 doorway line (already unsealed at the room-split boundary,
    // and already passing next to every airlock/interior-door fixture on that row) plus one
    // vertical spur per row-0 device. Skips (0,2)/(5,2)/(11,2) themselves since StationAirlock/
    // InteriorDoor/DerelictAirlock already occupy those tiles and bridge the spine via plain
    // tile-adjacency (see PowerSystem.AreConnected) — no conduit needed on top of them.
    private static readonly Vector2I[] DefaultConduitRoute =
    [
        new(1, 2), new(2, 2), new(3, 2), new(4, 2), new(6, 2), new(7, 2), new(8, 2), new(9, 2), new(10, 2),
        new(0, 1), // StationAirlock (0,2) -> TravelConsole (0,0)
        new(4, 1), // spine (4,2) -> Battery (4,0)
        new(5, 1), // InteriorDoor (5,2) -> Switch (5,0)
        new(9, 1), // spine (9,2) -> RechargeStation (9,0)
    ];

    private enum AimKind { None, Tile, Ceiling, Edge }

    // Internal, not private: BatteryVerbTarget/ToggleLightVerbTarget/RechargeStationVerbTarget
    // (own scripts, own collider) reference it to ask for their own removal verbs — see
    // MachineRemovalVerbs/ExecuteMachineRemoval below.
    internal enum MachineType { Battery, Switch, RechargeStation }

    private enum PendingAction
    {
        InstallConduit,
        RemoveConduit,
        BuildWall,
        RepairHullWall,
        RemoveWall,
        BreachHullWall,
        InstallFloor,
        RemoveFloor,
        InstallCeiling,
        RemoveCeiling,
        ExtendFloor,
        InstallMachine,
        UninstallMachine,
        ScrapMachine,
    }

    /// <summary>One tile can carry several conduits at once — one floor-mounted (WallNeighbor
    /// null) plus up to <see cref="WallSlotCount"/> per bordering wall (WallNeighbor = the cell
    /// across that specific edge, WallSlot = which height band on that wall face). They're
    /// deliberately small boxes rather than one big one, so a busy junction tile can hold multiple
    /// distinct wire runs (e.g. a corner turn, or several stacked by height) without them blocking
    /// each other — Scavengineers.Sim doesn't care: same tile, or a neighbor exactly one tile away,
    /// both already connect via PowerSystem's adjacency rule regardless of mount surface or height.
    /// WallSlot is meaningless (always the default) for a floor-mounted slot.</summary>
    private readonly record struct ConduitSlot(Vector2I Tile, CellCoord? WallNeighbor, int WallSlot = 0)
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

    /// <summary>Shared 1-tile flat mesh for both floor and ceiling panels — same shape, different
    /// position/material per instance.</summary>
    [Export]
    public Mesh? PanelMesh { get; set; }

    /// <summary>Shared full-tile (1x1, not PanelMesh's cosmetic 0.98 inset) collision box for
    /// each panel's own dedicated CollisionShape3D — this is what actually blocks/allows
    /// movement per tile now (see RefreshFloorPanelState/RefreshCeilingPanelState). The Home
    /// Ship's Floor/Ceiling no longer have a single unsplit collider at all; a raycast-only
    /// "aim helper" body (see World.tscn's Ceiling/FloorAimHelper, on the build_aim_only physics
    /// layer) is what lets the player still target a fully-enclosed hole to repair it.</summary>
    [Export]
    public Shape3D? PanelCollisionShape { get; set; }

    [Export]
    public Material? FloorPanelMaterial { get; set; }

    [Export]
    public Material? CeilingPanelMaterial { get; set; }

    /// <summary>The grid column of the room-split boundary (i.e. the edge between
    /// column-1 and column) — excluded from wall targeting entirely, since
    /// InteriorDoorVerbTarget already owns the two doorway edges there and would desync if this
    /// generic tool could silently reseal/unseal them too. -1 disables the exclusion.</summary>
    [Export]
    public int ExcludedEdgeColumn { get; set; } = -1;

    /// <summary>Ceiling height for every cell NOT in a west/east corridor strip (see
    /// <see cref="IsCorridorCell"/>) — corridor cells always use the plain
    /// <see cref="CeilingPanelHeight"/>. -1 (default) means "no override," matching
    /// CeilingPanelHeight everywhere, same as every ship before Station needed a taller room than
    /// its own corridor.</summary>
    [Export]
    public float TallCeilingHeight { get; set; } = -1f;

    /// <summary>True for any ship whose current boundary/interior wall layout (and, see
    /// <see cref="BatteryMesh"/>, its Battery/Switch/RechargeStation) should be seeded as real,
    /// player-removable structure once at startup (see <see cref="SeedDefaultShipLayout"/>),
    /// through the exact same helpers a save replay uses — the Home Ship and the Derelict both
    /// set this. Station doesn't: it never seeds or accepts any structural change at all (see
    /// <see cref="AllowStructuralModification"/>), so its hand-authored hull stays exactly as
    /// placed.</summary>
    [Export]
    public bool SeedDefaultLayout { get; set; }

    /// <summary>False makes <see cref="ResolveAvailableVerbs"/> return nothing regardless of aim
    /// state — for a ship the player is never meant to be able to alter (the Station:
    /// thematically, tampering with it is a crime). True (the default) everywhere else. Doesn't
    /// affect <see cref="GenerateFloorCeilingPanels"/> — Station still gets real per-tile panels
    /// for a consistent look, it just never offers a verb to touch them.</summary>
    [Export]
    public bool AllowStructuralModification { get; set; } = true;

    // Battery/Switch/RechargeStation meshes/shapes/materials — only wired on ships that opted
    // into the machine-construction-part system (currently just the Home Ship), same "null means
    // skip this feature entirely" pattern PanelMesh already uses for floor/ceiling. Gated behind
    // BatteryMesh specifically wherever only one flag is needed.
    [Export]
    public Mesh? BatteryMesh { get; set; }

    [Export]
    public Shape3D? BatteryShape { get; set; }

    [Export]
    public Material? BatteryMaterial { get; set; }

    [Export]
    public Mesh? SwitchMesh { get; set; }

    [Export]
    public Shape3D? SwitchShape { get; set; }

    [Export]
    public Material? SwitchMaterial { get; set; }

    [Export]
    public Mesh? RechargeStationMesh { get; set; }

    [Export]
    public Shape3D? RechargeStationShape { get; set; }

    [Export]
    public Material? RechargeStationMaterial { get; set; }

    /// <summary>The Home Ship's single room light — wired directly to a dynamically spawned
    /// Switch's own TargetLight, since it's no longer a fixed sibling node the switch's own
    /// scene declaration can NodePath to.</summary>
    [Export]
    public Light3D? RoomLight { get; set; }

    /// <summary>Generic dropped-item visual (reused for every item id) — used by AddOrDrop when a
    /// refund/scrap yield doesn't fully fit in the player's inventory, see InventoryOverflow.
    /// Same shared box PickupItem's own pre-placed world instances already use, just wired here
    /// too since a runtime-spawned pickup has no scene-authored mesh child of its own.</summary>
    [Export]
    public Mesh? DroppedItemMesh { get; set; }

    [Export]
    public Shape3D? DroppedItemShape { get; set; }

    [Export]
    public Material? DroppedItemMaterial { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private readonly Dictionary<ConduitSlot, Node3D> _placedConduits = new();
    private readonly Dictionary<(CellCoord, CellCoord), (MeshInstance3D Mesh, CollisionShape3D Collision)> _placedWalls = new();
    private readonly Dictionary<CellCoord, (MeshInstance3D Mesh, CollisionShape3D Collision)> _floorPanels = new();
    private readonly Dictionary<CellCoord, (MeshInstance3D Mesh, CollisionShape3D Collision)> _ceilingPanels = new();

    /// <summary>Cells added beyond the ship's default footprint via <see cref="ExtendFloor"/> —
    /// as opposed to every cell ShipSim's own GridWidth/corridor exports generate at startup.
    /// Tracked separately so CaptureBuildState/ApplyBuildState/ClearAllBuildState know exactly
    /// which cells to persist and which to actually remove on a load that doesn't include them.</summary>
    private readonly HashSet<CellCoord> _extendedCells = new();

    /// <summary>At most one of each <see cref="MachineType"/> at a time, matching ShipSim's own
    /// singular _battery field and fixed Switch/RechargeFixtureId — installing a second one
    /// elsewhere isn't offered while one already exists (see ResolveAvailableVerbs).</summary>
    private readonly Dictionary<MachineType, (CellCoord EdgeA, CellCoord EdgeB, Node3D Node)> _placedMachines = new();

    private Timer? _cycleTimer;
    private MeshInstance3D? _ghost;
    private bool _cycling;
    private Verb? _previewVerb;

    private AimKind _aimKind;
    private Vector2I _aimedTile;
    private CellCoord _edgeA;
    private CellCoord _edgeB;
    private int _aimedWallSlot;

    private PendingAction _pendingAction;
    private ConduitSlot _pendingSlot;
    private Vector2I _pendingTile;
    private CellCoord _pendingEdgeA;
    private CellCoord _pendingEdgeB;
    private MachineType _pendingMachineType;
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

        // ConduitMesh is optional (see GenerateFloorCeilingPanels's own PanelMesh-only gate) —
        // a ship with no conduit system (e.g. Station) has nothing to preview, and a null Mesh
        // has zero surfaces, so overriding surface 0 would throw.
        _ghost = new MeshInstance3D { Mesh = ConduitMesh, Visible = false };
        if (ConduitMesh is not null)
        {
            _ghost.SetSurfaceOverrideMaterial(0, GhostMaterial);
        }

        AddChild(_ghost);

        // Deferred: ShipSimRef's own Deck is built in its _Ready(), which may not have run yet
        // at this exact point depending on scene-tree sibling order (the established fix for
        // this same class of ordering issue elsewhere in the project, e.g. ShipSim's own
        // deferred vacuum seeding).
        CallDeferred(nameof(GenerateFloorCeilingPanels));
        if (SeedDefaultLayout)
        {
            CallDeferred(nameof(SeedDefaultShipLayout));
        }
    }

    private void GenerateFloorCeilingPanels()
    {
        // PanelMesh is only wired up on ships that opt into the floor/ceiling construction-part
        // system (currently just the Home Ship) — everything else skips this entirely rather
        // than generating unusable null-mesh instances.
        if (ShipSimRef is null || PanelMesh is null)
        {
            return;
        }

        foreach (var cell in ShipSimRef.Deck.Cells)
        {
            GeneratePanelsForCell(cell);
        }
    }

    /// <summary>One cell's floor+ceiling panel pair — extracted so a dynamically extended cell
    /// (see <see cref="ExtendFloor"/>) can get the exact same real, removable structure as every
    /// other cell already generated at startup, not a separate one-off representation.</summary>
    private void GeneratePanelsForCell(CellCoord cell)
    {
        var tile = new Vector2I(cell.X, cell.Y);

        var floorPanel = new MeshInstance3D { Mesh = PanelMesh };
        AddChild(floorPanel);
        floorPanel.Position = ToLocal(TileWorldPosition(tile, FloorPanelHeight));
        floorPanel.SetSurfaceOverrideMaterial(0, FloorPanelMaterial);

        var floorCollision = new CollisionShape3D { Shape = PanelCollisionShape };
        AddChild(floorCollision);
        floorCollision.Position = floorPanel.Position;

        _floorPanels[cell] = (floorPanel, floorCollision);

        var ceilingPanel = new MeshInstance3D { Mesh = PanelMesh };
        AddChild(ceilingPanel);
        ceilingPanel.Position = ToLocal(TileWorldPosition(tile, CeilingHeightFor(cell)));
        ceilingPanel.SetSurfaceOverrideMaterial(0, CeilingPanelMaterial);

        var ceilingCollision = new CollisionShape3D { Shape = PanelCollisionShape };
        AddChild(ceilingCollision);
        ceilingCollision.Position = ceilingPanel.Position;

        _ceilingPanels[cell] = (ceilingPanel, ceilingCollision);
    }

    private bool IsCorridorCell(CellCoord cell) =>
        ShipSimRef is not null && (cell.X < 0 || cell.X >= ShipSimRef.GridWidth);

    private float CeilingHeightFor(CellCoord cell) =>
        TallCeilingHeight > 0 && !IsCorridorCell(cell) ? TallCeilingHeight : CeilingPanelHeight;

    /// <summary>Claims a brand-new real Deck cell beyond the ship's existing footprint —
    /// genuine dynamic ship expansion, not just repairing an existing breached panel. The edge
    /// back to <paramref name="origin"/> becomes a normal open interior connection (unsealed by
    /// default, same as any other doorway); <see cref="NormalizeBoundaryEdgesForCell"/> handles
    /// clearing that edge's now-stale wall-breach flag and marking the new cell's other, still-
    /// open sides. Only the floor is claimed — the ceiling starts breached (open), same as any
    /// other missing ceiling, so it needs its own separate Install Ceiling verb rather than
    /// coming for free.</summary>
    private void ExtendFloor(CellCoord origin, CellCoord newCell)
    {
        ShipSimRef!.Deck.AddCell(newCell);
        ShipSimRef.Atmosphere?.AddCell(newCell);
        GeneratePanelsForCell(newCell);
        ShipSimRef.Deck.BreachHull(newCell, StructuralSurface.Ceiling);
        RefreshCeilingPanelState(new Vector2I(newCell.X, newCell.Y));
        _extendedCells.Add(newCell);
        NormalizeBoundaryEdgesForCell(newCell);
    }

    /// <summary>For each of a cell's 4 neighbors: if it's a real cell, the edge is interior now —
    /// clear any wall-breach flag left over from before it was extended into (a no-op if there
    /// never was one). If it's still not a real cell, mark that edge open — no wall has been
    /// built there yet. Checking generically like this (rather than tracking which direction was
    /// "the origin") means the exact same call works for save-replay too, where cells may have
    /// been extended in an arbitrary chain.</summary>
    private void NormalizeBoundaryEdgesForCell(CellCoord cell)
    {
        CellCoord[] neighbors =
        [
            new CellCoord(cell.X + 1, cell.Y),
            new CellCoord(cell.X - 1, cell.Y),
            new CellCoord(cell.X, cell.Y + 1),
            new CellCoord(cell.X, cell.Y - 1),
        ];

        foreach (var neighbor in neighbors)
        {
            if (ShipSimRef!.Deck.Cells.Contains(neighbor))
            {
                ShipSimRef.Deck.RepairWallEdge(cell, neighbor);
            }
            else
            {
                ShipSimRef.Deck.BreachWallEdge(cell, neighbor);
            }
        }
    }

    /// <summary>A ship's current wall layout, materialized as real player-removable structure
    /// instead of fixed scene geometry — boundary walls (breach-based), and one interior
    /// room-split wall per entry in <see cref="ShipSim.RoomSplitColumns"/> (edge-based), both
    /// leaving <see cref="DoorwayRows"/> open for airlocks/interior doors. Skips spawning a
    /// segment on any edge already marked wall-breached (see <see cref="MaybeSpawnWall"/>) — a
    /// ship seeded with a starting breach (e.g. the Derelict's <see cref="ShipSim.HasHullBreaches"/>)
    /// stays open there instead of getting walled over.</summary>
    private void SeedDefaultShipLayout()
    {
        if (ShipSimRef is null)
        {
            return;
        }

        var gridWidth = ShipSimRef.GridWidth;
        var gridDepth = ShipSim.GridDepth;

        for (var i = 0; i < gridWidth; i++)
        {
            MaybeSpawnWall(new CellCoord(i, 0), new CellCoord(i, -1));
            MaybeSpawnWall(new CellCoord(i, gridDepth - 1), new CellCoord(i, gridDepth));
        }

        foreach (var j in Enumerable.Range(0, gridDepth).Where(row => !DoorwayRows.Contains(row)))
        {
            MaybeSpawnWall(new CellCoord(0, j), new CellCoord(-1, j));
            MaybeSpawnWall(new CellCoord(gridWidth - 1, j), new CellCoord(gridWidth, j));

            foreach (var splitColumn in ShipSimRef.RoomSplitColumns)
            {
                var a = new CellCoord(splitColumn - 1, j);
                var b = new CellCoord(splitColumn, j);
                ShipSimRef.Deck.SealEdge(a, b);
                MaybeSpawnWall(a, b);
            }
        }

        // Airlock corridors — a narrow (DoorwayRows-only) strip of real cells attached to the
        // west/east boundary (see ShipSim.WestCorridorLength/EastCorridorLength). Only the long
        // sides need walling: the inner junction already falls out of the boundary-wall loop
        // above (rows 2/3 there were always left open for the doorway, and now lead into real
        // corridor cells instead of nothing), and the outer tip needs no wall at all since the
        // AirlockDoorVerbTarget's own frame already caps it, same as it did at the old boundary.
        for (var i = -1; i >= -ShipSimRef.WestCorridorLength; i--)
        {
            MaybeSpawnWall(new CellCoord(i, 2), new CellCoord(i, 1));
            MaybeSpawnWall(new CellCoord(i, 3), new CellCoord(i, 4));
        }

        for (var i = gridWidth; i < gridWidth + ShipSimRef.EastCorridorLength; i++)
        {
            MaybeSpawnWall(new CellCoord(i, 2), new CellCoord(i, 1));
            MaybeSpawnWall(new CellCoord(i, 3), new CellCoord(i, 4));
        }

        // Battery/Switch/RechargeStation only exist on ships that opted into the machine-
        // construction-part system (BatteryMesh wired) — same gate MachineVerbsFor uses.
        if (BatteryMesh is not null)
        {
            InstallBattery(BatteryEdge.A, BatteryEdge.B, savedState: null);
            InstallSwitch(SwitchEdge.A, SwitchEdge.B, savedState: null);
            InstallRechargeStation(RechargeStationEdge.A, RechargeStationEdge.B, savedState: null);

            // Wire the whole default layout together (see DefaultConduitRoute) — real,
            // player-removable conduits through the same InstallConduit a player's own wiring
            // verb and a save replay use, not a special-cased shortcut.
            foreach (var tile in DefaultConduitRoute)
            {
                InstallConduit(new ConduitSlot(tile, null));
            }
        }
    }

    /// <summary>Spawns a wall segment unless this edge already starts wall-breached (see
    /// <see cref="ShipSim.HasHullBreaches"/>) — a starting breach is a real, already-open gap,
    /// not scaffolding that gets walled over and then immediately needs repairing.</summary>
    private void MaybeSpawnWall(CellCoord a, CellCoord b)
    {
        if (!ShipSimRef!.Deck.IsWallEdgeBreached(a, b))
        {
            SpawnWallSegment(a, b);
        }
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

        if (ShipSimRef is null)
        {
            _aimKind = AimKind.None;
            UpdateGhostTransform();
            return;
        }

        if (Mathf.Min(distX, distZ) < EdgeMargin)
        {
            _aimKind = AimKind.Edge;

            CellCoord near;
            CellCoord far;
            if (distX <= distZ)
            {
                var neighborI = withinX < 0.5f ? i - 1 : i + 1;
                near = new CellCoord(i, j);
                far = new CellCoord(neighborI, j);
            }
            else
            {
                var neighborJ = withinZ < 0.5f ? j - 1 : j + 1;
                near = new CellCoord(i, j);
                far = new CellCoord(i, neighborJ);
            }

            // The raw hit point's own cell is normally the "inside" one, but a wall's collider
            // has real thickness — once an adjacent wall is gone, the player can end up standing
            // in that gap and aim at the next wall over from its outward face instead, which
            // flips which side the hit point falls on. Swap so _edgeA always ends up as whichever
            // side is real ship structure, regardless of which face actually got hit.
            if (!ShipSimRef.Deck.Cells.Contains(near) && ShipSimRef.Deck.Cells.Contains(far))
            {
                (near, far) = (far, near);
            }

            if (!ShipSimRef.Deck.Cells.Contains(near))
            {
                _aimKind = AimKind.None;
            }
            else
            {
                _edgeA = near;
                _edgeB = far;
                _aimedTile = new Vector2I(near.X, near.Y);
                _aimedWallSlot = Mathf.Clamp(Mathf.FloorToInt(local.Y / WallSlotHeight), 0, WallSlotCount - 1);

                if (IsExcludedColumn(_edgeA, _edgeB))
                {
                    _aimKind = AimKind.None;
                }
            }
        }
        else
        {
            _aimKind = ShipSimRef.Deck.Cells.Contains(new CellCoord(i, j)) ? AimKind.Tile : AimKind.None;
        }

        UpdateGhostTransform();
    }

    /// <summary>Ceiling aim is always tile-scoped (there's no "edge" concept looking straight up)
    /// — called instead of <see cref="SetAimPoint"/> when the raycast hit a ceiling's
    /// <see cref="ShipBuildAimForwarder"/> (its IsCeiling flag), routed from Player.</summary>
    public void SetCeilingAimPoint(Vector3 worldPoint)
    {
        var local = (ShipRoot ?? GetParent<Node3D>()).ToLocal(worldPoint);
        var tile = new CellCoord(Mathf.FloorToInt(local.X + 3), Mathf.FloorToInt(local.Z + 3));
        _aimedTile = new Vector2I(tile.X, tile.Y);
        _aimKind = ShipSimRef is not null && ShipSimRef.Deck.Cells.Contains(tile) ? AimKind.Ceiling : AimKind.None;
        UpdateGhostTransform();
    }

    /// <summary>Which install verb (if any) is currently highlighted — Player calls this every
    /// frame once it knows the active, affordable verb here. Drives both the ghost's visibility
    /// and its shape, since e.g. "install a conduit" and "build a wall" need different previews
    /// at the same aim point (see <see cref="UpdateGhostTransform"/>).</summary>
    public void SetPreviewVerb(Verb? verb)
    {
        _previewVerb = verb;
        UpdateGhostTransform();
    }

    // Only the two doorway edges (matching DoorwayRows) are InteriorDoorVerbTarget's to own —
    // the rest of this column is a real, solid wall (MidWallA/B) that this generic tool must
    // still manage, same as any other interior wall. Checking a.Y (== b.Y for this edge shape, a
    // column boundary between same-row cells) previously wasn't done at all, which excluded the
    // *entire* column from wall/conduit targeting, not just its two doorway rows.
    private bool IsExcludedColumn(CellCoord a, CellCoord b) =>
        ExcludedEdgeColumn >= 0 &&
        DoorwayRows.Contains(a.Y) &&
        ((a.X == ExcludedEdgeColumn - 1 && b.X == ExcludedEdgeColumn) ||
         (b.X == ExcludedEdgeColumn - 1 && a.X == ExcludedEdgeColumn));

    private IReadOnlyList<Verb> ResolveAvailableVerbs()
    {
        if (ShipSimRef is null || !AllowStructuralModification)
        {
            return [];
        }

        switch (_aimKind)
        {
            case AimKind.Tile:
            {
                var verbs = new List<Verb> { _placedConduits.ContainsKey(new ConduitSlot(_aimedTile, null)) ? RemoveConduitVerb : InstallConduitVerb };

                // Floor panels are only a real, visualized thing on ships that opted into the
                // system (PanelMesh configured) — everything else keeps its fixed floor.
                if (PanelMesh is not null)
                {
                    var floorPresent = !ShipSimRef.Deck.IsHullBreached(new CellCoord(_aimedTile.X, _aimedTile.Y), StructuralSurface.Floor);
                    verbs.Add(floorPresent ? RemoveFloorVerb : InstallFloorVerb);
                }

                return verbs;
            }

            case AimKind.Ceiling when PanelMesh is not null:
            {
                var ceilingPresent = !ShipSimRef.Deck.IsHullBreached(new CellCoord(_aimedTile.X, _aimedTile.Y), StructuralSurface.Ceiling);
                return [ceilingPresent ? RemoveCeilingVerb : InstallCeilingVerb];
            }

            case AimKind.Edge when !ShipSimRef.Deck.Cells.Contains(_edgeB):
            {
                // Boundary edge — Install repairs an existing breach, Remove deliberately
                // creates one (a real, consequential choice, same as scrapping the floor or
                // ceiling). A wall-mounted conduit needs an actual hull wall to mount on, so
                // it's only offered while that hull is intact. Tracked per edge, not per cell
                // (see Deck.BreachWallEdge) — a cell can have several independently open wall
                // directions at once, most visibly a freshly extended floor tile.
                var breached = ShipSimRef.Deck.IsWallEdgeBreached(_edgeA, _edgeB);
                var verbs = new List<Verb> { breached ? InstallWallVerb : RemoveWallVerb };
                if (breached)
                {
                    verbs.Add(ExtendFloorVerb);
                }

                if (EdgeConduitVerb(wallPresent: !breached) is { } boundaryConduitVerb)
                {
                    verbs.Add(boundaryConduitVerb);
                }

                verbs.AddRange(MachineVerbsFor(wallPresent: !breached));
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

                verbs.AddRange(MachineVerbsFor(wallPresent: sealed_));
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
        var slot = new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot);
        if (_placedConduits.ContainsKey(slot))
        {
            return RemoveConduitVerb;
        }

        return wallPresent ? InstallConduitVerb : null;
    }

    private static readonly MachineType[] AllMachineTypes = [MachineType.Battery, MachineType.Switch, MachineType.RechargeStation];

    /// <summary>Same multi-verb-on-one-edge idea as <see cref="EdgeConduitVerb"/> — a machine
    /// already at this exact edge offers Uninstall/Scrap; an empty, wall-present edge offers
    /// Install for every machine type not already placed *anywhere* on the ship (at most one of
    /// each — see <see cref="_placedMachines"/>). Every Install verb is returned regardless of
    /// what's currently held; IsAffordable (Player.cs) is what actually narrows the visible cycle
    /// down to whichever one item the player has in hand, same reliance
    /// StationConsoleVerbTarget's always-present Buy verbs already use. Gated behind BatteryMesh
    /// so ships that never opted into this system (Derelict/Station) never offer it at all.</summary>
    private IEnumerable<Verb> MachineVerbsFor(bool wallPresent)
    {
        if (BatteryMesh is null || !wallPresent)
        {
            yield break;
        }

        var here = _placedMachines.FirstOrDefault(kv => kv.Value.EdgeA == _edgeA && kv.Value.EdgeB == _edgeB);
        if (here.Value.Node is not null)
        {
            yield return UninstallVerbFor(here.Key);
            yield return ScrapVerbFor(here.Key);
            yield break;
        }

        foreach (var type in AllMachineTypes.Where(t => !_placedMachines.ContainsKey(t)))
        {
            yield return InstallVerbFor(type);
        }
    }

    private static Verb InstallVerbFor(MachineType type) => type switch
    {
        MachineType.Battery => InstallBatteryVerb,
        MachineType.Switch => InstallSwitchVerb,
        MachineType.RechargeStation => InstallRechargeStationVerb,
        _ => throw new System.ArgumentOutOfRangeException(nameof(type)),
    };

    private static Verb UninstallVerbFor(MachineType type) => type switch
    {
        MachineType.Battery => UninstallBatteryVerb,
        MachineType.Switch => UninstallSwitchVerb,
        MachineType.RechargeStation => UninstallRechargeStationVerb,
        _ => throw new System.ArgumentOutOfRangeException(nameof(type)),
    };

    private static Verb ScrapVerbFor(MachineType type) => type switch
    {
        MachineType.Battery => ScrapBatteryVerb,
        MachineType.Switch => ScrapSwitchVerb,
        MachineType.RechargeStation => ScrapRechargeStationVerb,
        _ => throw new System.ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Which machine type/pending action a machine verb id maps to — Action is null for
    /// any non-machine verb id, the signal ExecuteVerb/IsMachineVerb use to fall through.</summary>
    private static (MachineType Type, PendingAction? Action) ResolveMachineVerb(Verb verb) => verb.Id switch
    {
        _ when verb.Id == InstallBatteryVerb.Id => (MachineType.Battery, PendingAction.InstallMachine),
        _ when verb.Id == UninstallBatteryVerb.Id => (MachineType.Battery, PendingAction.UninstallMachine),
        _ when verb.Id == ScrapBatteryVerb.Id => (MachineType.Battery, PendingAction.ScrapMachine),
        _ when verb.Id == InstallSwitchVerb.Id => (MachineType.Switch, PendingAction.InstallMachine),
        _ when verb.Id == UninstallSwitchVerb.Id => (MachineType.Switch, PendingAction.UninstallMachine),
        _ when verb.Id == ScrapSwitchVerb.Id => (MachineType.Switch, PendingAction.ScrapMachine),
        _ when verb.Id == InstallRechargeStationVerb.Id => (MachineType.RechargeStation, PendingAction.InstallMachine),
        _ when verb.Id == UninstallRechargeStationVerb.Id => (MachineType.RechargeStation, PendingAction.UninstallMachine),
        _ when verb.Id == ScrapRechargeStationVerb.Id => (MachineType.RechargeStation, PendingAction.ScrapMachine),
        _ => (default, null),
    };

    private static bool IsMachineVerb(Verb verb) => ResolveMachineVerb(verb).Action is not null;

    private void ExecuteMachineVerb(Verb verb, PlayerInventory inventory)
    {
        var (type, action) = ResolveMachineVerb(verb);
        if (action is null)
        {
            return;
        }

        _pendingAction = action.Value;
        _pendingMachineType = type;
        _pendingEdgeA = _edgeA;
        _pendingEdgeB = _edgeB;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    /// <summary>Uninstall/Scrap for an already-installed machine, exposed so its own VerbTarget
    /// script (BatteryVerbTarget etc.) can merge them into its own AvailableVerbs — aiming
    /// directly at the machine's own box hits *its* collider, not this edge, so without this it
    /// has no way to offer removal at all alongside its native verb (Recharge/Toggle/...).
    /// Empty if that type isn't currently installed (shouldn't happen — a machine only ever asks
    /// about its own type while it exists — but a defensive empty list beats a throw).</summary>
    internal IReadOnlyList<Verb> MachineRemovalVerbs(MachineType type) =>
        _placedMachines.ContainsKey(type) ? [UninstallVerbFor(type), ScrapVerbFor(type)] : [];

    /// <summary>Counterpart to <see cref="MachineRemovalVerbs"/> — a machine's own ExecuteVerb
    /// delegates here for any verb id it doesn't recognize as its own. Looks the edge up from
    /// _placedMachines directly rather than this body's own _edgeA/_edgeB (which reflect whatever
    /// *this* target was last aimed at, not necessarily this machine — the player is aiming at
    /// the machine's own collider when this runs, not this one's).</summary>
    internal void ExecuteMachineRemoval(MachineType type, Verb verb, PlayerInventory inventory)
    {
        if (_cycling || !_placedMachines.TryGetValue(type, out var placed))
        {
            return;
        }

        PendingAction action;
        if (verb.Id == UninstallVerbFor(type).Id)
        {
            action = PendingAction.UninstallMachine;
        }
        else if (verb.Id == ScrapVerbFor(type).Id)
        {
            action = PendingAction.ScrapMachine;
        }
        else
        {
            return;
        }

        _pendingAction = action;
        _pendingMachineType = type;
        _pendingEdgeA = placed.EdgeA;
        _pendingEdgeB = placed.EdgeB;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    private void InstallMachine(MachineType type, CellCoord edgeA, CellCoord edgeB, string? savedState)
    {
        switch (type)
        {
            case MachineType.Battery:
                InstallBattery(edgeA, edgeB, savedState);
                break;
            case MachineType.Switch:
                InstallSwitch(edgeA, edgeB, savedState);
                break;
            case MachineType.RechargeStation:
                InstallRechargeStation(edgeA, edgeB, savedState);
                break;
        }
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

        if (IsMachineVerb(verb))
        {
            ExecuteMachineVerb(verb, inventory);
            return;
        }

        if (verb.Id == InstallFloorVerb.Id || verb.Id == RemoveFloorVerb.Id)
        {
            ExecutePanelVerb(verb, InstallFloorVerb.Id, StructuralSurface.Floor,
                PendingAction.InstallFloor, PendingAction.RemoveFloor, inventory);
            return;
        }

        if (verb.Id == InstallCeilingVerb.Id || verb.Id == RemoveCeilingVerb.Id)
        {
            ExecutePanelVerb(verb, InstallCeilingVerb.Id, StructuralSurface.Ceiling,
                PendingAction.InstallCeiling, PendingAction.RemoveCeiling, inventory);
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
            ? new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot)
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

    private void ExecutePanelVerb(Verb verb, string installId, StructuralSurface surface,
        PendingAction installAction, PendingAction removeAction, PlayerInventory inventory)
    {
        var present = !ShipSimRef!.Deck.IsHullBreached(new CellCoord(_aimedTile.X, _aimedTile.Y), surface);
        if ((verb.Id == installId) == present)
        {
            return; // Install only valid while missing, Remove only valid while present.
        }

        _pendingAction = verb.Id == installId ? installAction : removeAction;
        _pendingTile = _aimedTile;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    private void ExecuteWallVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != InstallWallVerb.Id && verb.Id != RemoveWallVerb.Id && verb.Id != ExtendFloorVerb.Id)
        {
            return;
        }

        var isBoundary = !ShipSimRef!.Deck.Cells.Contains(_edgeB);
        if (isBoundary)
        {
            var breached = ShipSimRef.Deck.IsWallEdgeBreached(_edgeA, _edgeB);
            if (verb.Id == InstallWallVerb.Id && breached)
            {
                _pendingAction = PendingAction.RepairHullWall;
            }
            else if (verb.Id == RemoveWallVerb.Id && !breached)
            {
                _pendingAction = PendingAction.BreachHullWall;
            }
            else if (verb.Id == ExtendFloorVerb.Id && breached)
            {
                _pendingAction = PendingAction.ExtendFloor;
            }
            else
            {
                return;
            }
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
                AddOrDrop(inventory, "scrap_metal", 1);
                break;
            case PendingAction.BuildWall:
                ShipSimRef!.Deck.SealEdge(_pendingEdgeA, _pendingEdgeB);
                SpawnWallSegment(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.RepairHullWall:
                ShipSimRef!.Deck.RepairWallEdge(_pendingEdgeA, _pendingEdgeB);
                SpawnWallSegment(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.RemoveWall:
                ShipSimRef!.Deck.UnsealEdge(_pendingEdgeA, _pendingEdgeB);
                FreeWallSegment(_pendingEdgeA, _pendingEdgeB);
                AddOrDrop(inventory, "wall_panel", 1);
                break;
            case PendingAction.BreachHullWall:
                ShipSimRef!.Deck.BreachWallEdge(_pendingEdgeA, _pendingEdgeB);
                FreeWallSegment(_pendingEdgeA, _pendingEdgeB);
                AddOrDrop(inventory, "wall_panel", 1);
                break;
            case PendingAction.ExtendFloor:
                ExtendFloor(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.InstallFloor:
                ShipSimRef!.Deck.RepairHull(new CellCoord(_pendingTile.X, _pendingTile.Y), StructuralSurface.Floor);
                RefreshFloorPanelState(_pendingTile);
                break;
            case PendingAction.RemoveFloor:
                ShipSimRef!.Deck.BreachHull(new CellCoord(_pendingTile.X, _pendingTile.Y), StructuralSurface.Floor);
                RefreshFloorPanelState(_pendingTile);
                AddOrDrop(inventory, "wall_panel", 1);
                break;
            case PendingAction.InstallCeiling:
                ShipSimRef!.Deck.RepairHull(new CellCoord(_pendingTile.X, _pendingTile.Y), StructuralSurface.Ceiling);
                RefreshCeilingPanelState(_pendingTile);
                break;
            case PendingAction.RemoveCeiling:
                ShipSimRef!.Deck.BreachHull(new CellCoord(_pendingTile.X, _pendingTile.Y), StructuralSurface.Ceiling);
                RefreshCeilingPanelState(_pendingTile);
                AddOrDrop(inventory, "wall_panel", 1);
                break;
            case PendingAction.InstallMachine:
                InstallMachine(_pendingMachineType, _pendingEdgeA, _pendingEdgeB, savedState: null);
                break;
            case PendingAction.UninstallMachine:
                RemoveMachine(_pendingMachineType);
                AddOrDrop(inventory, ItemIdFor(_pendingMachineType), 1);
                break;
            case PendingAction.ScrapMachine:
                RemoveMachine(_pendingMachineType);
                AddOrDrop(inventory, "scrap_metal", ScrapYieldFor(_pendingMachineType));
                break;
        }
    }

    /// <summary>Adds a refund/scrap-yield item to the player's inventory, dropping whatever
    /// doesn't fit as a world pickup right where this action happened instead of losing it —
    /// unlike picking something up that's already a world object (see PickupItem's own partial-
    /// pickup handling), a refund has nothing else to fall back to but a freshly spawned one.</summary>
    private void AddOrDrop(PlayerInventory? inventory, string itemId, int count)
    {
        if (inventory is null)
        {
            return;
        }

        var added = inventory.Add(itemId, count);
        if (added < count && DroppedItemMesh is not null && DroppedItemShape is not null)
        {
            InventoryOverflow.DropAt(this, itemId, count - added, DroppedItemMesh, DroppedItemShape, DroppedItemMaterial);
        }
    }

    /// <summary>Syncs a floor tile to the current Deck state: hides the panel mesh and disables
    /// its own dedicated collision while breached — a real, actually-open hole (you can see and
    /// fly/fall through it), not a different-colored but still-solid panel.</summary>
    private void RefreshFloorPanelState(Vector2I tile)
    {
        var cell = new CellCoord(tile.X, tile.Y);
        if (!_floorPanels.TryGetValue(cell, out var panel))
        {
            return;
        }

        var breached = ShipSimRef?.Deck.IsHullBreached(cell, StructuralSurface.Floor) ?? false;
        panel.Mesh.Visible = !breached;
        panel.Collision.Disabled = breached;
    }

    /// <summary>Ceiling counterpart of <see cref="RefreshFloorPanelState"/>.</summary>
    private void RefreshCeilingPanelState(Vector2I tile)
    {
        var cell = new CellCoord(tile.X, tile.Y);
        if (!_ceilingPanels.TryGetValue(cell, out var panel))
        {
            return;
        }

        var breached = ShipSimRef?.Deck.IsHullBreached(cell, StructuralSurface.Ceiling) ?? false;
        panel.Mesh.Visible = !breached;
        panel.Collision.Disabled = breached;
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
        container.Position = ToLocal(TileWorldPosition(tile, FloorConduitHeight));

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
        var (position, loneRotation) = WallConduitTransform(edgeA, wallNeighbor, tile, slot.WallSlot);
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
            // Same height slot only — a run only reads as continuous between wires mounted at the
            // same band on adjacent wall tiles, not just anything else on that wall.
            if (!_placedConduits.ContainsKey(new ConduitSlot(neighborTile, neighborWallNeighbor, slot.WallSlot)))
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
            // directly against that hub's actual position rather than an assumed fixed offset.
            // Routed down the wall/corner FIRST (staying at the wall's own horizontal position,
            // no reach yet) and only jogging out to the floor conduit's hub once it's already at
            // floor height — a real wire runs down a corner then along the floor, it doesn't
            // jut straight out into open air at wall height and drop through nothing.
            hasArm = true;

            var floorHubLocal = ToLocal(TileWorldPosition(tile, FloorConduitHeight));
            var delta = floorHubLocal - container.Position;
            var horizontalDelta = new Vector3(delta.X, 0, delta.Z);
            var horizontalDistance = horizontalDelta.Length();

            var drop = new MeshInstance3D { Mesh = ConduitDropMesh };
            drop.SetSurfaceOverrideMaterial(0, ConduitMaterial);
            drop.Scale = new Vector3(1, Mathf.Abs(delta.Y) / WallToFloorDropHeight, 1);
            drop.Position = new Vector3(0, delta.Y / 2f, 0);
            container.AddChild(drop);

            if (horizontalDistance > 0.01f)
            {
                // ConduitArmMesh's own authored length is 0.5 — scale it to whatever the real
                // horizontal distance is instead of assuming it's always exactly one value.
                var reachOut = new MeshInstance3D { Mesh = ConduitArmMesh };
                reachOut.SetSurfaceOverrideMaterial(0, ConduitMaterial);
                reachOut.Scale = new Vector3(1, 1, horizontalDistance / 0.5f);
                reachOut.Position = new Vector3(horizontalDelta.X / 2f, delta.Y, horizontalDelta.Z / 2f);
                reachOut.RotationDegrees = Mathf.Abs(delta.X) > Mathf.Abs(delta.Z) ? new Vector3(0, 90, 0) : Vector3.Zero;
                container.AddChild(reachOut);
            }
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
                RefreshWallConduitVisual(new ConduitSlot(neighborTile, neighborWallNeighbor, slot.WallSlot));
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

    /// <summary>The world/local height of a wall conduit's given height slot, 0-indexed bottom to
    /// top — slot count is derived from <see cref="WallHeight"/>, not fixed, so both the count
    /// and each slot's actual height stay correct automatically if <see cref="WallHeight"/> ever
    /// changes again (today: <see cref="WallSlotCount"/> is 2, matching a 2m wall).</summary>
    private static float SlotHeight(int slot) => (slot + 0.5f) * WallSlotHeight;

    /// <summary>Same edge position/rotation a wall segment would use, nudged toward whichever tile
    /// the mount belongs to (reads as mounted on that tile's wall face instead of embedded in the
    /// wall itself) and raised/lowered to the given height instead of the wall's fixed center.
    /// Shared by conduits (height/offset from the slot system) and machines (their own fixed
    /// per-type height/offset, see BatteryHeight etc.) — the only difference between mounting a
    /// wire and mounting a battery on the same wall is how far up and how far out it sits.</summary>
    private (Vector3 Position, Vector3 RotationDegrees) WallMountTransform(CellCoord edgeA, CellCoord edgeB, Vector2I nearTile, float height, float roomOffset)
    {
        var (position, rotationDegrees) = EdgeTransform(edgeA, edgeB);
        position.Y = height;

        var midX = (edgeA.X + edgeB.X) / 2f;
        var midY = (edgeA.Y + edgeB.Y) / 2f;
        var offset = new Vector3(
            Mathf.Sign(nearTile.X - midX) * roomOffset,
            0,
            Mathf.Sign(nearTile.Y - midY) * roomOffset);

        return (position + offset, rotationDegrees);
    }

    private (Vector3 Position, Vector3 RotationDegrees) WallConduitTransform(CellCoord edgeA, CellCoord edgeB, Vector2I nearTile, int slot) =>
        WallMountTransform(edgeA, edgeB, nearTile, SlotHeight(slot), WallMountRoomOffset);

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

    /// <summary>Builds the Battery's own interactable node (BatteryVerbTarget, mesh, collision,
    /// plus its charge IndicatorLight — the same child <see cref="PoweredDeviceIndicator"/> setup
    /// World.tscn used to hand-author), mounts it at the given edge, and registers its fixture
    /// with ShipSim. <paramref name="savedState"/> (BatteryVerbTarget's own charge-fraction save
    /// string), if given, is applied directly to the freshly-built instance — no group-scan save
    /// mechanism is involved, since a dynamically spawned machine is never in the "saveable"
    /// group (see docs/plan — ShipBuildTarget's own save record owns this instead).</summary>
    private void InstallBattery(CellCoord edgeA, CellCoord edgeB, string? savedState)
    {
        var nearTile = new Vector2I(edgeA.X, edgeA.Y);
        var (position, rotation) = WallMountTransform(edgeA, edgeB, nearTile, BatteryHeight, BatteryRoomOffset);

        var node = new BatteryVerbTarget { ShipSimRef = ShipSimRef, BuildTarget = this };
        AddChild(node);
        node.Position = position;
        node.RotationDegrees = rotation;

        var mesh = new MeshInstance3D { Mesh = BatteryMesh };
        mesh.SetSurfaceOverrideMaterial(0, BatteryMaterial);
        node.AddChild(mesh);

        node.AddChild(new CollisionShape3D { Shape = BatteryShape });

        var indicatorLight = new OmniLight3D
        {
            Position = new Vector3(0, 0.6f, 0),
            LightColor = new Color(0.95f, 0.75f, 0.1f),
            OmniRange = 2f,
            Visible = false,
        };
        node.AddChild(indicatorLight);

        node.AddChild(new PoweredDeviceIndicator
        {
            ShipSimRef = ShipSimRef,
            FixtureId = ShipSim.BatteryFixtureId,
            IndicatorLight = indicatorLight,
        });

        ShipSimRef!.InstallBattery(edgeA, FixtureSurface.WallInner);

        if (savedState is not null)
        {
            node.ApplySaveState(savedState);
        }

        _placedMachines[MachineType.Battery] = (edgeA, edgeB, node);
    }

    /// <summary>Same shape as <see cref="InstallBattery"/> — RoomLight is wired directly (no
    /// NodePath, since a dynamically spawned node has no fixed sibling to path to) and
    /// <paramref name="savedState"/> is the switch's own on/off bool, stringified.</summary>
    private void InstallSwitch(CellCoord edgeA, CellCoord edgeB, string? savedState)
    {
        var nearTile = new Vector2I(edgeA.X, edgeA.Y);
        var (position, rotation) = WallMountTransform(edgeA, edgeB, nearTile, SwitchHeight, SwitchRoomOffset);

        var node = new ToggleLightVerbTarget { ShipSimRef = ShipSimRef, TargetLight = RoomLight, BuildTarget = this };
        AddChild(node);
        node.Position = position;
        node.RotationDegrees = rotation;

        var mesh = new MeshInstance3D { Mesh = SwitchMesh };
        mesh.SetSurfaceOverrideMaterial(0, SwitchMaterial);
        node.AddChild(mesh);

        node.AddChild(new CollisionShape3D { Shape = SwitchShape });

        ShipSimRef!.InstallSwitch(edgeA, FixtureSurface.WallInner);

        if (savedState is not null && bool.TryParse(savedState, out var isOn))
        {
            node.ApplySaveState(isOn);
        }

        _placedMachines[MachineType.Switch] = (edgeA, edgeB, node);
    }

    /// <summary>Same shape as <see cref="InstallBattery"/>/<see cref="InstallSwitch"/> — the
    /// Recharge Station has no extra state of its own, so <paramref name="savedState"/> is unused
    /// (kept for a uniform three-way call signature, see ApplyBuildState/SeedDefaultShipLayout).</summary>
    private void InstallRechargeStation(CellCoord edgeA, CellCoord edgeB, string? savedState)
    {
        var nearTile = new Vector2I(edgeA.X, edgeA.Y);
        var (position, rotation) = WallMountTransform(edgeA, edgeB, nearTile, RechargeStationHeight, RechargeStationRoomOffset);

        var node = new RechargeStationVerbTarget { ShipSimRef = ShipSimRef, BuildTarget = this };
        AddChild(node);
        node.Position = position;
        node.RotationDegrees = rotation;

        var mesh = new MeshInstance3D { Mesh = RechargeStationMesh };
        mesh.SetSurfaceOverrideMaterial(0, RechargeStationMaterial);
        node.AddChild(mesh);

        node.AddChild(new CollisionShape3D { Shape = RechargeStationShape });

        ShipSimRef!.InstallRechargeStation(edgeA, FixtureSurface.WallInner);

        _placedMachines[MachineType.RechargeStation] = (edgeA, edgeB, node);
    }

    private void RemoveMachine(MachineType type)
    {
        if (!_placedMachines.Remove(type, out var placed))
        {
            return;
        }

        placed.Node.QueueFree();

        switch (type)
        {
            case MachineType.Battery:
                ShipSimRef?.RemoveBattery();
                break;
            case MachineType.Switch:
                ShipSimRef?.RemoveSwitch();
                break;
            case MachineType.RechargeStation:
                ShipSimRef?.RemoveRechargeStation();
                break;
        }
    }

    /// <summary>The machine's own extra state to round-trip through a save (battery charge,
    /// switch on/off) — null for a stateless machine (Recharge Station) or an unrecognized type.</summary>
    private static string? MachineStateOf(MachineType type, Node3D node) => type switch
    {
        MachineType.Battery => ((BatteryVerbTarget)node).GetSaveState(),
        MachineType.Switch => ((ToggleLightVerbTarget)node).GetSaveState().ToString(),
        _ => null,
    };

    private static string ItemIdFor(MachineType type) => type switch
    {
        MachineType.Battery => "battery",
        MachineType.Switch => "switch",
        MachineType.RechargeStation => "recharge_station",
        _ => throw new System.ArgumentOutOfRangeException(nameof(type)),
    };

    // Placeholder/tunable — roughly matches each machine's relative buy price (see
    // StationConsoleVerbTarget.Prices).
    private static int ScrapYieldFor(MachineType type) => type switch
    {
        MachineType.Battery => 4,
        MachineType.Switch => 1,
        MachineType.RechargeStation => 3,
        _ => 1,
    };

    /// <summary>Ghost shape/position depends on both where you're aiming AND which install verb
    /// is currently highlighted — e.g. "install a conduit" and "build a floor panel" are
    /// different objects sharing the same tile aim point, so previewing the wrong one's shape
    /// would be actively misleading, not just imprecise. Always shows the plain lone/panel-box
    /// shape rather than a live connection-aware preview — a rough "here's where" indicator, not
    /// a promise of the final shape.</summary>
    private void UpdateGhostTransform()
    {
        var isInstall = _previewVerb == InstallConduitVerb || _previewVerb == InstallWallVerb ||
                         _previewVerb == InstallFloorVerb || _previewVerb == InstallCeilingVerb ||
                         _previewVerb == ExtendFloorVerb;
        if (!isInstall)
        {
            _ghost!.Visible = false;
            return;
        }

        switch (_aimKind)
        {
            case AimKind.Tile when _previewVerb == InstallFloorVerb:
                _ghost!.Visible = true;
                _ghost.Mesh = PanelMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileWorldPosition(_aimedTile, FloorPanelHeight));
                break;

            case AimKind.Tile:
                _ghost!.Visible = true;
                _ghost.Mesh = ConduitMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileWorldPosition(_aimedTile, FloorConduitHeight));
                break;

            case AimKind.Ceiling:
                _ghost!.Visible = true;
                _ghost.Mesh = PanelMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileWorldPosition(_aimedTile, CeilingPanelHeight));
                break;

            case AimKind.Edge when _previewVerb == ExtendFloorVerb:
                // Previews the new cell itself (across the edge, at _edgeB) rather than the
                // edge — that's where the floor panel will actually land, same shape as the
                // Tile/InstallFloorVerb preview above.
                _ghost!.Visible = true;
                _ghost.Mesh = PanelMesh;
                _ghost.RotationDegrees = Vector3.Zero;
                _ghost.Position = ToLocal(TileWorldPosition(new Vector2I(_edgeB.X, _edgeB.Y), FloorPanelHeight));
                break;

            case AimKind.Edge when _previewVerb == InstallConduitVerb:
            {
                _ghost!.Visible = true;
                _ghost.Mesh = WallConduitMesh;
                var (position, rotationDegrees) = WallConduitTransform(_edgeA, _edgeB, new Vector2I(_edgeA.X, _edgeA.Y), _aimedWallSlot);
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
    private static Vector3 EdgeShipLocalPosition(CellCoord a, CellCoord b)
    {
        var midX = (a.X + b.X) / 2f;
        var midY = (a.Y + b.Y) / 2f;
        return new Vector3(midX - 3 + 0.5f, WallCenterHeight, midY - 3 + 0.5f);
    }

    private (Vector3 Position, Vector3 RotationDegrees) EdgeTransform(CellCoord a, CellCoord b)
    {
        var world = (ShipRoot ?? GetParent<Node3D>()).ToGlobal(EdgeShipLocalPosition(a, b));

        // WallSegmentMesh is authored running along X (separates cells differing in Y) — rotate
        // 90 degrees when the edge instead separates cells differing in X.
        var rotationDegrees = a.X != b.X ? new Vector3(0, 90, 0) : Vector3.Zero;

        return (ToLocal(world), rotationDegrees);
    }

    /// <summary>World position of an edge's midpoint — the wall-breach counterpart to
    /// <see cref="TileWorldPosition"/>, reusing <see cref="EdgeTransform"/>'s own position math so
    /// a breach pull target always lines up with the actual wall segment.</summary>
    private Vector3 EdgeWorldPosition(CellCoord a, CellCoord b) =>
        (ShipRoot ?? GetParent<Node3D>()).ToGlobal(EdgeShipLocalPosition(a, b));

    private Vector3 TileWorldPosition(Vector2I tile, float height)
    {
        var shipLocal = new Vector3(tile.X - 3 + 0.5f, height, tile.Y - 3 + 0.5f);
        return (ShipRoot ?? GetParent<Node3D>()).ToGlobal(shipLocal);
    }

    /// <summary>World positions of every currently-open floor/ceiling/wall breach on this ship —
    /// the decompression-pull hazard's own read of Deck.HullBreaches/WallEdgeBreaches, reusing the
    /// exact same TileWorldPosition/EdgeWorldPosition math GenerateFloorCeilingPanels/SpawnWallSegment
    /// already use so a pull target always lines up with the actual hole, not a re-derived
    /// approximation.</summary>
    public IEnumerable<Vector3> ActiveBreachPositions()
    {
        if (ShipSimRef is null)
        {
            yield break;
        }

        foreach (var cell in ShipSimRef.Deck.HullBreaches)
        {
            var tile = new Vector2I(cell.X, cell.Y);

            if (ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Floor))
            {
                yield return TileWorldPosition(tile, FloorPanelHeight);
            }

            if (ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Ceiling))
            {
                yield return TileWorldPosition(tile, CeilingPanelHeight);
            }
        }

        // Floor/ceiling breaches are tracked per-cell (handled above); a breached/removed wall is
        // tracked per-edge instead (Deck.WallEdgeBreaches, not the per-cell _breaches set), so it
        // needs its own pass rather than fitting into IsHullBreached(cell, surface) above.
        foreach (var (a, b) in ShipSimRef.Deck.WallEdgeBreaches)
        {
            yield return EdgeWorldPosition(a, b);
        }
    }

    private static string ConduitFixtureId(ConduitSlot slot) => slot.WallNeighbor is { } neighbor
        ? $"player_conduit_{slot.Tile.X}_{slot.Tile.Y}_wall_{neighbor.X}_{neighbor.Y}_slot{slot.WallSlot}"
        : $"player_conduit_{slot.Tile.X}_{slot.Tile.Y}_floor";

    public BuildTargetSaveData CaptureBuildState()
    {
        var data = new BuildTargetSaveData();

        foreach (var slot in _placedConduits.Keys)
        {
            if (slot.WallNeighbor is { } neighbor)
            {
                data.WallConduits.Add(new WallConduitCoord(slot.Tile.X, slot.Tile.Y, neighbor.X, neighbor.Y, slot.WallSlot));
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

        foreach (var cell in _floorPanels.Keys)
        {
            if (ShipSimRef!.Deck.IsHullBreached(cell, StructuralSurface.Floor))
            {
                data.FloorBreaches.Add(new TileCoord(cell.X, cell.Y));
            }
        }

        foreach (var cell in _ceilingPanels.Keys)
        {
            if (ShipSimRef!.Deck.IsHullBreached(cell, StructuralSurface.Ceiling))
            {
                data.CeilingBreaches.Add(new TileCoord(cell.X, cell.Y));
            }
        }

        foreach (var (type, placed) in _placedMachines)
        {
            data.Machines.Add(new MachineCoord(ItemIdFor(type), placed.EdgeA.X, placed.EdgeA.Y, placed.EdgeB.X, placed.EdgeB.Y, MachineStateOf(type, placed.Node)));
        }

        foreach (var cell in _extendedCells)
        {
            data.ExtendedCells.Add(new TileCoord(cell.X, cell.Y));
        }

        return data;
    }

    private static MachineType? MachineTypeFromItemId(string itemId) => itemId switch
    {
        "battery" => MachineType.Battery,
        "switch" => MachineType.Switch,
        "recharge_station" => MachineType.RechargeStation,
        _ => null,
    };

    /// <summary>Replays a save's tiles/edges through the same helpers Install/BuildWall use —
    /// already inventory-free at this level (the verb/cost logic lives in ExecuteVerb, never
    /// called here), so restoring a save never re-charges scrap_metal/wall_panel. Clears all
    /// current conduit/wall/floor/ceiling state first: the ship's own default layout (see
    /// <see cref="SeedDefaultShipLayout"/>) is itself now removable, so a loaded save must be
    /// authoritative rather than layered on top of whatever startup already built.</summary>
    public void ApplyBuildState(BuildTargetSaveData state)
    {
        ClearAllBuildState();

        // Two passes: every extended cell must exist in Deck.Cells before any of them normalize
        // their boundary edges, since a cell extended from another extended cell (a chain) needs
        // its neighbor to already be real to correctly tell "interior" from "still open."
        foreach (var tile in state.ExtendedCells)
        {
            var cell = new CellCoord(tile.X, tile.Y);
            if (!ShipSimRef!.Deck.Cells.Contains(cell))
            {
                ShipSimRef.Deck.AddCell(cell);
                ShipSimRef.Atmosphere?.AddCell(cell);
                GeneratePanelsForCell(cell);
                _extendedCells.Add(cell);
            }
        }

        foreach (var tile in state.ExtendedCells)
        {
            NormalizeBoundaryEdgesForCell(new CellCoord(tile.X, tile.Y));
        }

        foreach (var tile in state.Conduits)
        {
            InstallConduit(new ConduitSlot(new Vector2I(tile.X, tile.Y), null));
        }

        foreach (var wallConduit in state.WallConduits)
        {
            var tile = new Vector2I(wallConduit.TileX, wallConduit.TileY);
            var neighbor = new CellCoord(wallConduit.NeighborX, wallConduit.NeighborY);

            // Clamp rather than trust the saved value as-is — a save written before a wall-height
            // change (fewer/shorter slots today than when it was saved) could otherwise place a
            // conduit above the current ceiling; live aiming already clamps the same way.
            var slot = Mathf.Clamp(wallConduit.Slot, 0, WallSlotCount - 1);
            InstallConduit(new ConduitSlot(tile, neighbor, slot));
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
                ShipSimRef.Deck.RepairWallEdge(a, b);
            }

            SpawnWallSegment(a, b);
        }

        // ClearAllBuildState just breached every floor/ceiling tile as its own "removed"
        // baseline (mirroring how it unseals every wall) — repair them all back to intact
        // before selectively re-breaching only the ones the save actually recorded as open.
        // Previously this step was missing (breach was purely cosmetic, so nothing ever
        // surfaced it); now that breach means "no collision," skipping it would drop the
        // player through the entire floor and ceiling on every load except the handful of
        // tiles that happened to be breached when the save was made.
        foreach (var cell in _floorPanels.Keys)
        {
            ShipSimRef!.Deck.RepairHull(cell, StructuralSurface.Floor);
            RefreshFloorPanelState(new Vector2I(cell.X, cell.Y));
        }

        foreach (var cell in _ceilingPanels.Keys)
        {
            ShipSimRef!.Deck.RepairHull(cell, StructuralSurface.Ceiling);
            RefreshCeilingPanelState(new Vector2I(cell.X, cell.Y));
        }

        foreach (var tile in state.FloorBreaches)
        {
            ShipSimRef!.Deck.BreachHull(new CellCoord(tile.X, tile.Y), StructuralSurface.Floor);
            RefreshFloorPanelState(new Vector2I(tile.X, tile.Y));
        }

        foreach (var tile in state.CeilingBreaches)
        {
            ShipSimRef!.Deck.BreachHull(new CellCoord(tile.X, tile.Y), StructuralSurface.Ceiling);
            RefreshCeilingPanelState(new Vector2I(tile.X, tile.Y));
        }

        foreach (var machine in state.Machines)
        {
            if (MachineTypeFromItemId(machine.Type) is not { } type)
            {
                GD.PushWarning($"[ShipBuildTarget] Save references unknown machine type '{machine.Type}' — skipping.");
                continue;
            }

            InstallMachine(type, new CellCoord(machine.EdgeAX, machine.EdgeAY), new CellCoord(machine.EdgeBX, machine.EdgeBY), machine.State);
        }
    }

    private void ClearAllBuildState()
    {
        foreach (var (slot, visual) in _placedConduits)
        {
            ShipSimRef?.Deck.RemoveFixture(ConduitFixtureId(slot));
            visual.QueueFree();
        }

        _placedConduits.Clear();

        foreach (var ((a, b), pair) in _placedWalls)
        {
            ShipSimRef?.Deck.UnsealEdge(a, b);
            if (ShipSimRef is not null && !ShipSimRef.Deck.Cells.Contains(b))
            {
                ShipSimRef.Deck.BreachWallEdge(a, b);
            }

            pair.Mesh.QueueFree();
            pair.Collision.QueueFree();
        }

        _placedWalls.Clear();

        foreach (var cell in _floorPanels.Keys)
        {
            ShipSimRef?.Deck.BreachHull(cell, StructuralSurface.Floor);
            RefreshFloorPanelState(new Vector2I(cell.X, cell.Y));
        }

        foreach (var cell in _ceilingPanels.Keys)
        {
            ShipSimRef?.Deck.BreachHull(cell, StructuralSurface.Ceiling);
            RefreshCeilingPanelState(new Vector2I(cell.X, cell.Y));
        }

        foreach (var type in _placedMachines.Keys.ToList())
        {
            RemoveMachine(type);
        }

        // A load that doesn't include a previously-extended cell must actually remove it —
        // Load() (SaveManager.cs) applies state onto the live scene, not a fresh reload, so a
        // stale extension from earlier in the same session would otherwise just linger.
        foreach (var cell in _extendedCells)
        {
            if (_floorPanels.Remove(cell, out var floorPanel))
            {
                floorPanel.Mesh.QueueFree();
                floorPanel.Collision.QueueFree();
            }

            if (_ceilingPanels.Remove(cell, out var ceilingPanel))
            {
                ceilingPanel.Mesh.QueueFree();
                ceilingPanel.Collision.QueueFree();
            }

            ShipSimRef?.Deck.RemoveCell(cell);
            ShipSimRef?.Atmosphere?.RemoveCell(cell);
        }

        _extendedCells.Clear();
    }
}
