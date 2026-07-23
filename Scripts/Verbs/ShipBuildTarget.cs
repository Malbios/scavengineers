using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>Turns a ship's whole Floor into a free-form build target — any tile, ceiling point,
/// or edge the player is aiming at (fed in via <see cref="SetAimPoint"/>/
/// <see cref="SetCeilingAimPoint"/>) can have a conduit (tile or wall), a floor panel (tile), a
/// ceiling panel, or a wall segment (edge) installed/removed. Reuses PowerSystem's adjacency rule
/// for conduits, Deck.SealEdge/UnsealEdge for interior walls, and Deck.BreachHull/RepairHull for
/// both boundary walls and floor/ceiling — no new Scavengineers.Sim concepts needed.</summary>
public partial class ShipBuildTarget : StaticBody3D, IVerbTarget, IBuildTargetSaveable
{
    // Public so Player can compare its filtered/affordable verb against these exact instances to
    // decide when the placement ghost should show, without duplicating verb ids as strings.
    public static readonly Verb InstallConduitVerb = new("install_conduit", "VERB_INSTALL_CONDUIT", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("scrap_metal", 1)],
    };

    // Wall/floor/ceiling work (not conduits) needs a power drill in hand alongside the consumed
    // material — a real tool, not spent, gated on its own charge.
    private static readonly ItemRequirement PowerDrillRequirement = new("power_drill", 1) { Consumed = false };

    public static readonly Verb InstallWallVerb = new("build_wall", "VERB_BUILD_WALL", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveConduitVerb = new("remove_conduit", "VERB_REMOVE_CONDUIT", DurationSeconds: 0.6f) { IsDestructive = true };

    private static readonly Verb RemoveWallVerb = new("remove_wall", "VERB_REMOVE_WALL", DurationSeconds: 0.6f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    // Floor and ceiling panels reuse the same wall_panel construction-part item as walls.
    private static readonly Verb InstallFloorVerb = new("install_floor", "VERB_INSTALL_FLOOR", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveFloorVerb = new("remove_floor", "VERB_REMOVE_FLOOR", DurationSeconds: 0.6f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    private static readonly Verb InstallCeilingVerb = new("install_ceiling", "VERB_INSTALL_CEILING", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    private static readonly Verb RemoveCeilingVerb = new("remove_ceiling", "VERB_REMOVE_CEILING", DurationSeconds: 0.6f)
    {
        IsDestructive = true,
        Requirements = [PowerDrillRequirement],
    };

    // Same cost as InstallFloorVerb, but claims a brand-new cell instead of repairing an existing
    // one's panel. Only offered alongside InstallWallVerb on a boundary edge that's currently
    // open — you extend through a gap you've made, not through an intact wall.
    private static readonly Verb ExtendFloorVerb = new("extend_floor", "VERB_EXTEND_FLOOR", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("wall_panel", 1), PowerDrillRequirement],
    };

    // The two-tier upkeep model: above 50% health, a wrench alone tops a surface back to full —
    // no resource cost. At or below 50% it's Damaged and needs the wrench *and* spare_parts.
    // Offered alongside, not instead of, the existing Install/Remove verb above; reaching exactly
    // 0% health is a full breach, which stays on that verb since there's no panel left to top up.
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainFloorVerb = new("maintain_floor", "VERB_MAINTAIN_FLOOR", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairFloorVerb = new("repair_floor", "VERB_REPAIR_FLOOR", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    private static readonly Verb MaintainCeilingVerb = new("maintain_ceiling", "VERB_MAINTAIN_CEILING", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairCeilingVerb = new("repair_ceiling", "VERB_REPAIR_CEILING", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    private static readonly Verb MaintainWallVerb = new("maintain_wall", "VERB_MAINTAIN_WALL", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairWallVerb = new("repair_wall", "VERB_REPAIR_WALL", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    // Same two-tier upkeep for a placed conduit fixture — offered alongside its Remove verb.
    private static readonly Verb MaintainConduitVerb = new("maintain_conduit", "VERB_MAINTAIN_CONDUIT", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairConduitVerb = new("repair_conduit", "VERB_REPAIR_CONDUIT", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    /// <summary>Everything that varies between the single-instance machines (Battery/Switch/
    /// Recharge Station) except the bits that are genuinely per-machine code: the mesh/material/
    /// collider it spawns and which VerbTarget script it attaches (see <see cref="InstallBattery"/>
    /// and its two siblings). The five verbs are generated rather than listed, since the ids and
    /// localization keys are perfectly systematic (<c>install_&lt;item&gt;</c> /
    /// <c>VERB_INSTALL_&lt;ITEM&gt;</c>, and so on for uninstall/scrap/maintain/repair). Install
    /// requires holding the machine's item; Uninstall gives it back; Scrap yields partial
    /// scrap_metal instead — a real tradeoff.</summary>
    /// <param name="FixtureId">The Deck fixture backing this machine's wear/Condition, or null for
    /// no upkeep concept at all — null is exactly Battery, whose Condition already means charge,
    /// not wear, so it gets no Maintain/Repair pair.</param>
    private sealed record MachineDefinition(MachineType Type, string ItemId, string? FixtureId, int ScrapYield)
    {
        public Verb Install { get; } = new($"install_{ItemId}", $"VERB_INSTALL_{ItemId.ToUpperInvariant()}", DurationSeconds: 0.6f)
        {
            Requirements = [new ItemRequirement(ItemId, 1)],
        };

        public Verb Uninstall { get; } =
            new($"uninstall_{ItemId}", $"VERB_UNINSTALL_{ItemId.ToUpperInvariant()}", DurationSeconds: 0.6f) { IsDestructive = true };

        public Verb Scrap { get; } =
            new($"scrap_{ItemId}", $"VERB_SCRAP_{ItemId.ToUpperInvariant()}", DurationSeconds: 0.6f) { IsDestructive = true };

        /// <summary>Null iff <see cref="FixtureId"/> is.</summary>
        public Verb? Maintain { get; } = FixtureId is null
            ? null
            : new Verb($"maintain_{ItemId}", $"VERB_MAINTAIN_{ItemId.ToUpperInvariant()}", DurationSeconds: 0.6f)
            {
                Requirements = [WrenchRequirement],
            };

        public Verb? Repair { get; } = FixtureId is null
            ? null
            : new Verb($"repair_{ItemId}", $"VERB_REPAIR_{ItemId.ToUpperInvariant()}", DurationSeconds: 0.6f)
            {
                Requirements = [WrenchRequirement, SparePartsRequirement],
            };
    }

    // ScrapYield is placeholder/tunable — roughly tracks each machine's own buy price in items.json.
    private static readonly Dictionary<MachineType, MachineDefinition> Machines = new()
    {
        [MachineType.Battery] = new(MachineType.Battery, "battery", FixtureId: null, ScrapYield: 4),
        [MachineType.Switch] = new(MachineType.Switch, "switch", ShipSim.SwitchFixtureId, ScrapYield: 1),
        [MachineType.RechargeStation] = new(MachineType.RechargeStation, "recharge_station", ShipSim.RechargeFixtureId, ScrapYield: 3),
    };

    private static MachineDefinition Definition(MachineType type) => Machines[type];

    // Thruster — same Install/Uninstall/Scrap shape as Battery/Switch/RechargeStation, but NOT
    // MachineType-based: many can be installed at once, one per edge. Its own Refuel verb lives
    // on ThrusterVerbTarget instead, mirroring Battery's Recharge verb on BatteryVerbTarget.
    private static readonly Verb InstallThrusterVerb = new("install_thruster", "VERB_INSTALL_THRUSTER", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("thruster", 1)],
    };

    private static readonly Verb UninstallThrusterVerb = new("uninstall_thruster", "VERB_UNINSTALL_THRUSTER", DurationSeconds: 0.6f) { IsDestructive = true };
    private static readonly Verb ScrapThrusterVerb = new("scrap_thruster", "VERB_SCRAP_THRUSTER", DurationSeconds: 0.6f) { IsDestructive = true };

    // Storage (shelves/bins) — same "many per ship, one per edge" shape as Thruster above, but
    // Install is generated per catalog item (see MachineVerbsFor) rather than one static Verb per
    // tier, since the set of storage items is genuinely data-driven (ItemCatalog.StorageItemIds).
    // Uninstall/Scrap are shared across every tier — removal doesn't care which one it is.
    private static readonly Verb UninstallStorageVerb = new("uninstall_storage", "VERB_UNINSTALL_STORAGE", DurationSeconds: 0.6f) { IsDestructive = true };
    private static readonly Verb ScrapStorageVerb = new("scrap_storage", "VERB_SCRAP_STORAGE", DurationSeconds: 0.6f) { IsDestructive = true };

    // How close (in meters) the aim point needs to be to a tile boundary before it resolves to
    // that edge instead of the tile itself — half of this margin on each side of every boundary.
    private const float EdgeMargin = 0.25f;

    // The ship's shared coordinate system and the heights that depend on it live in ShipGeometry —
    // ShipAtmosphereZone needs the same mapping (it converts the other way, world position back to
    // tile), and it used to reimplement it from an unnamed literal. Aliased here so the many uses
    // below stay readable.
    private const float WallCenterHeight = ShipGeometry.WallCenterHeight;
    private const float FloorConduitHeight = ShipGeometry.FloorConduitHeight;
    private const float WallHeight = ShipGeometry.WallHeight;
    private const float FloorPanelHeight = ShipGeometry.FloorPanelHeight;
    private const float CeilingPanelHeight = ShipGeometry.CeilingPanelHeight;
    private static readonly int WallSlotCount = ShipGeometry.WallSlotCount;
    private static readonly float WallSlotHeight = ShipGeometry.WallSlotHeight;

    /// <summary>Kept as a public alias because World.tscn's authored deck transform and
    /// docs/architecture/ship-model.md both reference it by this name.</summary>
    public const float DeckYOffset = ShipGeometry.DeckYOffset;

    // Matches Derelict.tscn's own hand-placed pickups' resting height (e.g. WallPanel1/ScrapMetal
    // both sit at Y=0.15) — a bit more clearance than the purely-visual conduit mount since a
    // spawned RigidBody3D pickup needs room to settle physically without floor interpenetration.
    private const float LootRestHeight = 0.15f;

    // Atmosphere-zone generation: generic overlap margin so adjacent zones' box shapes touch/
    // slightly overlap rather than leaving a gap at their shared boundary —
    // ShipAtmosphereZone.FindZoneAt's containment-margin tie-break resolves any such overlap.
    private const float ZoneWidthOverlapMargin = 0.1f;
    private const float ZoneDepthOverlapMargin = 0.2f;
    private const float ZoneHeightOverlapMargin = 0.1f;

    /// <summary>Extra X-axis margin (replacing, not adding to, <see cref="ZoneWidthOverlapMargin"/>)
    /// on the edge of the band adjacent to a nonzero west/east airlock corridor length — guards
    /// the docking seam where a Derelict's corridor meets the Home Ship's own.</summary>
    private const float CorridorSeamOverlapMargin = 4.0f;

    // ConduitDropMesh's authored length — BuildWallConduitVisual scales a fresh instance to the
    // *actual* measured gap rather than assuming a fixed distance, so it can't silently drift out
    // of sync with the real geometry.
    private const float WallToFloorDropHeight = WallCenterHeight - FloorConduitHeight;
    private const float WallMountRoomOffset = 0.15f; // matches WallConduitTransform's own push

    // Each machine's own mount height/room-offset. Recharge Station sits further into the room
    // since it's a station you approach, not a flush wall fitting like the other two.
    private const float BatteryHeight = 1f;
    private const float BatteryRoomOffset = 0.1f;
    private const float SwitchHeight = 1f;
    private const float SwitchRoomOffset = 0.1f;
    private const float RechargeStationHeight = 0.5f;
    private const float RechargeStationRoomOffset = 0.3f;
    private const float ThrusterHeight = 1f;
    private const float ThrusterRoomOffset = 0.1f;
    private const float StorageHeight = 1f;
    private const float StorageRoomOffset = 0.1f;

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
    // doors — matches ShipSim's own DoorwayRows exactly (kept in sync by hand).
    private static readonly int[] DoorwayRows = [2, 3];

    // The Home Ship's default Battery/Switch/RechargeStation edges. Battery and Switch sit on
    // Manhattan-adjacent tiles — PowerSystem already treats directly-adjacent fixtures as
    // touching, no conduit segment needed between them.
    private static readonly (CellCoord A, CellCoord B) BatteryEdge = (new CellCoord(4, 0), new CellCoord(4, -1));
    private static readonly (CellCoord A, CellCoord B) SwitchEdge = (new CellCoord(5, 0), new CellCoord(5, -1));
    private static readonly (CellCoord A, CellCoord B) RechargeStationEdge = (new CellCoord(9, 0), new CellCoord(9, -1));

    // Two default thrusters, one per room (RoomSplitColumns = [6] splits room 1 at columns 0-5
    // from room 2 at 6-11) — free row-0 boundary columns.
    private static readonly (CellCoord A, CellCoord B) ThrusterEdge1 = (new CellCoord(3, 0), new CellCoord(3, -1));
    private static readonly (CellCoord A, CellCoord B) ThrusterEdge2 = (new CellCoord(8, 0), new CellCoord(8, -1));

    // Default wiring for the Home Ship's seeded layout — a straight utility spine along the
    // row-2 doorway line plus one vertical spur per row-0 device. Skips (0,2)/(5,2)/(11,2)
    // themselves since StationAirlock/InteriorDoor/DerelictAirlock already occupy those tiles and
    // bridge the spine via plain tile-adjacency — no conduit needed on top of them.
    private static readonly Vector2I[] DefaultConduitRoute =
    [
        new(1, 2), new(2, 2), new(3, 2), new(4, 2), new(6, 2), new(7, 2), new(8, 2), new(9, 2), new(10, 2),
        new(0, 1), // StationAirlock (0,2) -> TravelConsole (0,0)
        new(4, 1), // spine (4,2) -> Battery (4,0)
        new(5, 1), // InteriorDoor (5,2) -> Switch (5,0)
        new(9, 1), // spine (9,2) -> RechargeStation (9,0)
        new(3, 1), // spine (3,2) -> Thruster 1 (3,0)
        new(8, 1), // spine (8,2) -> Thruster 2 (8,0)
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
        InstallThruster,
        UninstallThruster,
        ScrapThruster,
        InstallStorage,
        UninstallStorage,
        ScrapStorage,
        MaintainFloor,
        RepairFloor,
        MaintainCeiling,
        RepairCeiling,
        MaintainWall,
        RepairWall,
        MaintainConduit,
        RepairConduit,
        MaintainMachine,
        RepairMachine,
    }

    /// <summary>One tile can carry several conduits at once — one floor-mounted (WallNeighbor
    /// null) plus up to <see cref="WallSlotCount"/> per bordering wall (WallNeighbor = the cell
    /// across that edge, WallSlot = which height band on that wall face). WallSlot is meaningless
    /// for a floor-mounted slot.</summary>
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
    /// T/cross shape (floor) or its along-wall/into-the-room stubs (wall) — thin and short enough
    /// that several fit on one tile without visually merging. Authored long-axis Z (north/south);
    /// rotated 90 degrees for east/west arms.</summary>
    [Export]
    public Mesh? ConduitArmMesh { get; set; }

    /// <summary>The vertical leg of a wall-to-floor connector — a fixed length matching
    /// <see cref="WallToFloorDropHeight"/>.</summary>
    [Export]
    public Mesh? ConduitDropMesh { get; set; }

    /// <summary>Distinct shape from <see cref="ConduitMesh"/> — thin front-to-back rather than
    /// top-to-bottom, so it reads as mounted flush against a wall face. Shown only for a wall
    /// conduit with no connections; connected ones use <see cref="ConduitArmMesh"/> instead.</summary>
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

    /// <summary>Shared full-tile (1x1, not PanelMesh's cosmetic 0.98 inset) collision box for each
    /// panel's own CollisionShape3D — this is what actually blocks/allows movement per tile now.
    /// A raycast-only "aim helper" body (see World.tscn's Ceiling/FloorAimHelper, on the
    /// build_aim_only physics layer) is what lets the player still target a fully-enclosed hole
    /// to repair it.</summary>
    [Export]
    public Shape3D? PanelCollisionShape { get; set; }

    [Export]
    public Material? FloorPanelMaterial { get; set; }

    [Export]
    public Material? CeilingPanelMaterial { get; set; }

    /// <summary>The grid column of the room-split boundary — excluded from wall targeting
    /// entirely, since InteriorDoorVerbTarget already owns the two doorway edges there and would
    /// desync if this generic tool could silently reseal/unseal them too. -1 disables the
    /// exclusion.</summary>
    [Export]
    public int ExcludedEdgeColumn { get; set; } = -1;

    /// <summary>Ceiling height for every cell NOT in a west/east corridor strip (see
    /// <see cref="IsCorridorCell"/>) — corridor cells always use the plain
    /// <see cref="CeilingPanelHeight"/>. -1 (default) means no override.</summary>
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

    /// <summary>True spawns one <see cref="ShipAtmosphereZone"/> per room band (between
    /// consecutive <see cref="ShipSim.RoomSplitColumns"/> entries) plus one for the west/east
    /// airlock corridor, procedurally from this ship's own current grid shape — see
    /// <see cref="GenerateAtmosphereZones"/>. Currently opt-in for the Derelict only: the Home
    /// Ship's zones are already hand-placed and working, and touching them adds regression risk
    /// for a system this project has already spent real debugging time getting right, for zero
    /// payoff toward the "different derelicts need different zone layouts" problem this exists
    /// to solve.</summary>
    [Export]
    public bool GenerateAtmosphereZones { get; set; }

    /// <summary>True spawns this ship's own ShipSim.LootSpawns as real world PickupItems (see
    /// <see cref="SpawnGeneratedLoot"/>) — the procedural-generation counterpart to
    /// Derelict.tscn's hand-placed pickups, which aren't (and can't be) tied to a data-driven
    /// layout's own footprint. Only ever set for a ship whose LootSpawns is actually populated
    /// (a procedurally-generated ship — see ShipSim.ProcedurallyGenerate); a no-op otherwise
    /// since ShipSimRef.LootSpawns is empty by default.</summary>
    [Export]
    public bool GenerateLoot { get; set; }

    /// <summary>False makes <see cref="ResolveAvailableVerbs"/> return nothing regardless of aim
    /// state — for a ship the player is never meant to be able to alter (the Station:
    /// thematically, tampering with it is a crime). True (the default) everywhere else. Doesn't
    /// affect <see cref="GenerateFloorCeilingPanels"/> — Station still gets real per-tile panels
    /// for a consistent look, it just never offers a verb to touch them.</summary>
    [Export]
    public bool AllowStructuralModification { get; set; } = true;

    // Battery/Switch/RechargeStation meshes/shapes/materials — only wired on ships that opted
    // into the machine-construction-part system (currently just the Home Ship), same "null means
    // skip this feature entirely" pattern PanelMesh uses for floor/ceiling.
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

    [Export]
    public Mesh? ThrusterMesh { get; set; }

    [Export]
    public Shape3D? ThrusterShape { get; set; }

    [Export]
    public Material? ThrusterMaterial { get; set; }

    // Shared across every storage tier (small_bin/shelf/large_shelf) rather than one set per tier
    // — visual differentiation per tier is a later art-pass concern, not this feature's.
    [Export]
    public Mesh? StorageMesh { get; set; }

    [Export]
    public Shape3D? StorageShape { get; set; }

    [Export]
    public Material? StorageMaterial { get; set; }

    /// <summary>The Home Ship's single room light — wired directly to a dynamically spawned
    /// Switch's own TargetLight, since it's no longer a fixed sibling node the switch's scene
    /// declaration can NodePath to.</summary>
    [Export]
    public Light3D? RoomLight { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private readonly Dictionary<ConduitSlot, Node3D> _placedConduits = new();
    private readonly Dictionary<(CellCoord, CellCoord), (MeshInstance3D Mesh, CollisionShape3D Collision)> _placedWalls = new();
    private readonly Dictionary<CellCoord, (MeshInstance3D Mesh, CollisionShape3D Collision)> _floorPanels = new();
    private readonly Dictionary<CellCoord, (MeshInstance3D Mesh, CollisionShape3D Collision)> _ceilingPanels = new();

    /// <summary>Cells added beyond the ship's default footprint via <see cref="ExtendFloor"/>.
    /// Tracked separately so CaptureBuildState/ApplyBuildState/ClearAllBuildState know exactly
    /// which cells to persist and which to remove on a load that doesn't include them.</summary>
    private readonly HashSet<CellCoord> _extendedCells = new();

    /// <summary>At most one of each <see cref="MachineType"/> at a time, matching ShipSim's own
    /// singular _battery field and fixed Switch/RechargeFixtureId.</summary>
    private readonly Dictionary<MachineType, (CellCoord EdgeA, CellCoord EdgeB, Node3D Node)> _placedMachines = new();

    /// <summary>Unlike <see cref="_placedMachines"/>, many thrusters can exist at once — keyed by
    /// <see cref="Deck.Normalize"/> of the mounting edge so aiming at the same interior wall from
    /// either side resolves to the same entry instead of allowing a duplicate install on the far
    /// side.</summary>
    private readonly Dictionary<(CellCoord, CellCoord), ThrusterVerbTarget> _placedThrusters = new();

    private readonly Dictionary<(CellCoord, CellCoord), StorageVerbTarget> _placedStorage = new();

    private Timer? _cycleTimer;
    private MeshInstance3D? _ghost;
    private bool _cycling;
    private Verb? _previewVerb;

    /// <summary>A second, dedicated mesh purely for the PDA scan-mode highlight (see
    /// HighlightVisual) — kept separate from _ghost, whose visibility/shape is tied to the
    /// currently-selected install-preview verb, not to "is anything sensible aimed at right now"
    /// in general. Starts on no render layer (Layers = 0) — invisible in the main view always;
    /// Player is the only thing that ever sets its highlight-layer bit.</summary>
    private MeshInstance3D? _highlightGhost;

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
    private string _pendingStorageItemId = "";
    private PlayerInventory? _pendingInventory;

    public IReadOnlyList<Verb> AvailableVerbs => ResolveAvailableVerbs();

    // No name label: the floor isn't a discrete object like the Computer or a hull breach, it's
    // just terrain you can build on — the ghost box alone communicates what's about to happen.

    public float? CurrentVerbProgress =>
        _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime) : null;

    /// <summary>The currently-aimed surface's health, for the PDA scan-mode crosshair readout —
    /// null while breached or while aiming at something with no structural-surface concept. A
    /// conduit fixture at the aimed slot wins over the underlying floor/wall (scanning the wire is
    /// more specific than scanning what it's mounted on) — never checked for AimKind.Ceiling,
    /// since a conduit slot is always floor/wall-mounted.</summary>
    public float? Condition
    {
        get
        {
            if (ShipSimRef is null)
            {
                return null;
            }

            if (_aimKind != AimKind.Ceiling && AimedConduitFixture() is { } conduitFixture)
            {
                return conduitFixture.Condition;
            }

            switch (_aimKind)
            {
                case AimKind.Tile when PanelMesh is not null:
                {
                    var cell = new CellCoord(_aimedTile.X, _aimedTile.Y);
                    return ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Floor) ? null : ShipSimRef.Deck.FloorHealth(cell);
                }

                case AimKind.Ceiling when PanelMesh is not null:
                {
                    var cell = new CellCoord(_aimedTile.X, _aimedTile.Y);
                    return ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Ceiling) ? null : ShipSimRef.Deck.CeilingHealth(cell);
                }

                case AimKind.Edge when !ShipSimRef.Deck.Cells.Contains(_edgeB):
                    return ShipSimRef.Deck.IsWallEdgeBreached(_edgeA, _edgeB) ? null : ShipSimRef.Deck.WallHealth(_edgeA, _edgeB);

                case AimKind.Edge:
                    return ShipSimRef.Deck.IsEdgeSealed(_edgeA, _edgeB) ? ShipSimRef.Deck.WallHealth(_edgeA, _edgeB) : null;

                default:
                    return null;
            }
        }
    }

    /// <summary>Overrides IVerbTarget's default (which looks for a fixed child mesh) — floor/
    /// wall/ceiling has no single mesh to point at, so this dynamically-repositioned mesh (see
    /// UpdateHighlightGhostTransform) stands in for whichever surface is currently aimed at.</summary>
    public IReadOnlyList<VisualInstance3D> HighlightVisual => _aimKind == AimKind.None ? [] : [_highlightGhost!];

    public override void _Ready()
    {
        _cycleTimer = new Timer { OneShot = true, WaitTime = InstallConduitVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

        // ConduitMesh is optional — a ship with no conduit system (e.g. Station) has nothing to
        // preview, and a null Mesh has zero surfaces, so overriding surface 0 would throw.
        _ghost = new MeshInstance3D { Mesh = ConduitMesh, Visible = false };
        if (ConduitMesh is not null)
        {
            _ghost.SetSurfaceOverrideMaterial(0, GhostMaterial);
        }

        AddChild(_ghost);

        _highlightGhost = new MeshInstance3D { Visible = true, Layers = 0 };
        AddChild(_highlightGhost);

        // Deferred: ShipSimRef's Deck is built in its own _Ready(), which may not have run yet at
        // this exact point depending on scene-tree sibling order.
        //
        // One deferred entry point rather than four separate CallDeferred queue entries: the four
        // steps have a required order among themselves (panels exist before zones are sized off
        // them, the default layout is seeded before loot lands on it), and four independent queue
        // entries only preserved that by luck. It also gives the whole batch a single completion
        // signal (see InitialGenerationComplete).
        CallDeferred(nameof(RunInitialGeneration));
    }

    /// <summary>True once <see cref="RunInitialGeneration"/> has finished. Exists for tests: this
    /// node's whole visible state appears during a deferred call, so a test that just awaits one
    /// ProcessFrame and asserts is racing the deferred flush.</summary>
    public bool InitialGenerationComplete { get; private set; }

    private void RunInitialGeneration()
    {
        GenerateFloorCeilingPanels();

        if (SeedDefaultLayout)
        {
            SeedDefaultShipLayout();
        }

        if (GenerateAtmosphereZones)
        {
            GenerateAtmosphereZonesFromRoomLayout();
        }

        if (GenerateLoot)
        {
            SpawnGeneratedLoot();
        }

        InitialGenerationComplete = true;
    }

    /// <summary>ToggleLightVerbTarget's own _PhysicsProcess is what keeps RoomLight in sync with
    /// power while a switch is installed — with no switch node to do that, RoomLight would
    /// otherwise just freeze at whatever it last showed. Uninstalling the switch isn't cutting the
    /// wire, just removing the toggle: the light should keep following raw ship power, not go dark
    /// for no electrical reason. Only takes over while no switch is placed at all — deliberately
    /// hands off entirely to the switch's own tick (including its manual on/off toggle) otherwise.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (RoomLight is not null && !_placedMachines.ContainsKey(MachineType.Switch))
        {
            RoomLight.Visible = ShipSimRef?.IsPowered(ShipSim.BatteryFixtureId) ?? false;
        }
    }

    /// <summary>Turns ShipSimRef's own procedurally-generated LootSpawns into real world
    /// PickupItems — reuses InventoryOverflow.DropAt (already used for refund/scrap-yield
    /// overflow), which builds each pickup's own visual from its ItemId (see
    /// ItemVisualBuilder/PickupItem), so this needs no visual/spawn code of its own.</summary>
    private void SpawnGeneratedLoot()
    {
        if (ShipSimRef is null)
        {
            return;
        }

        foreach (var loot in ShipSimRef.LootSpawns)
        {
            var worldPosition = TileWorldPosition(new Vector2I(loot.TileX, loot.TileY), LootRestHeight);
            InventoryOverflow.DropAt(this, loot.ItemId, loot.Count, worldPosition);
        }
    }

    /// <summary>Drops one contract-target item onto a random live, non-vented cell of this ship —
    /// called by ContractGiverVerbTarget the instant a RetrieveItem offer is accepted. Falls back
    /// to any cell (ignoring vent state) if the whole ship happens to be vented, rather than
    /// silently placing nothing.</summary>
    public void SpawnMissionItem(string itemId, int count, System.Random rng)
    {
        if (ShipSimRef is null || ShipSimRef.Deck.Cells.Count == 0)
        {
            return;
        }

        var cells = ShipSimRef.Deck.Cells.ToList();
        var liveCells = ShipSimRef.Atmosphere is { } atmosphere
            ? cells.Where(cell => !atmosphere.IsConnectedToOutside(cell)).ToList()
            : cells;

        var candidates = liveCells.Count > 0 ? liveCells : cells;
        var cell = candidates[rng.Next(candidates.Count)];
        var worldPosition = TileWorldPosition(new Vector2I(cell.X, cell.Y), LootRestHeight);

        PlaceMissionItem(itemId, count, charge: 1f, worldPosition);
    }

    /// <summary>The actual spawn, split out from <see cref="SpawnMissionItem"/> so save/load can
    /// recreate a still-outstanding mission item at its exact saved position instead of re-rolling
    /// a new random cell. Additionally mirrors this ship's CURRENT presence state (Visible/
    /// collision/physics) onto the new pickup — unlike a startup-time spawn, this can run on an
    /// already-hidden derelict at any point during play, and
    /// TravelConsoleVerbTarget.SetShipPresence's traversal only re-runs on a destination change —
    /// it won't retroactively catch a child added after the fact.</summary>
    public void PlaceMissionItem(string itemId, int count, float charge, Vector3 worldPosition)
    {
        var shipRoot = ShipRoot ?? GetParent<Node3D>();
        var pickup = new PickupItem { ItemId = itemId, Count = count, Charge = charge, MissionOwnerSaveId = SaveId };
        shipRoot.AddChild(pickup);
        pickup.GlobalPosition = worldPosition;

        var present = shipRoot.Visible;
        pickup.Visible = present;

        foreach (var node in pickup.FindChildren("*", nameof(CollisionShape3D), recursive: true, owned: false))
        {
            if (node is CollisionShape3D shape)
            {
                shape.Disabled = !present;
            }
        }

        pickup.SetPhysicsPresence(present);
    }

    private void GenerateFloorCeilingPanels()
    {
        // PanelMesh is only wired up on ships that opt into the floor/ceiling construction-part
        // system — everything else skips this entirely rather than generating unusable
        // null-mesh instances.
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
    /// (see <see cref="ExtendFloor"/>) gets the same real, removable structure as every other
    /// cell generated at startup.</summary>
    private void GeneratePanelsForCell(CellCoord cell)
    {
        var tile = new Vector2I(cell.X, cell.Y);

        // A ladder shaft's tile skips exactly one of its two panels — never both, and never via
        // Deck.BreachHull (that would wire this cell to the Outside vacuum sentinel, incorrectly
        // venting this deck since decks are independently simulated, not bridged). The primary
        // deck (DeckIndex 0) opens its ceiling here; a second deck opens its floor instead — each
        // still gets its OTHER panel normally.
        var isLadderCell = ShipSimRef?.LadderCell == cell;
        var skipFloor = isLadderCell && (ShipSimRef?.DeckIndex ?? 0) > 0;
        var skipCeiling = isLadderCell && (ShipSimRef?.DeckIndex ?? 0) == 0;

        if (!skipFloor)
        {
            var floorPanel = new MeshInstance3D { Mesh = PanelMesh };
            AddChild(floorPanel);
            floorPanel.Position = ToLocal(TileWorldPosition(tile, FloorPanelHeight));
            floorPanel.SetSurfaceOverrideMaterial(0, FloorPanelMaterial);

            var floorCollision = new CollisionShape3D { Shape = PanelCollisionShape };
            AddChild(floorCollision);
            floorCollision.Position = floorPanel.Position;

            _floorPanels[cell] = (floorPanel, floorCollision);
        }

        if (!skipCeiling)
        {
            var ceilingPanel = new MeshInstance3D { Mesh = PanelMesh };
            AddChild(ceilingPanel);
            ceilingPanel.Position = ToLocal(TileWorldPosition(tile, CeilingHeightFor(cell)));
            ceilingPanel.SetSurfaceOverrideMaterial(0, CeilingPanelMaterial);

            var ceilingCollision = new CollisionShape3D { Shape = PanelCollisionShape };
            AddChild(ceilingCollision);
            ceilingCollision.Position = ceilingPanel.Position;

            _ceilingPanels[cell] = (ceilingPanel, ceilingCollision);
        }
    }

    /// <summary>Spawns one <see cref="ShipAtmosphereZone"/> per room band (the span between
    /// consecutive <see cref="ShipSim.RoomSplitColumns"/> entries) plus one for each nonzero
    /// airlock corridor. Each zone is added as a sibling of this Floor node (a child of the
    /// ship's spatial root), matching <see cref="ShipAtmosphereZone.TileAt"/>'s assumption that
    /// its parent is always that root, not this Floor node's own local space.</summary>
    private void GenerateAtmosphereZonesFromRoomLayout()
    {
        if (ShipSimRef is null)
        {
            return;
        }

        var shipRoot = ShipRoot ?? GetParent<Node3D>();
        var gridWidth = ShipSimRef.GridWidth;
        var gridDepth = ShipSim.GridDepth;

        var boundaries = new List<int> { 0 };
        boundaries.AddRange(ShipSimRef.RoomSplitColumns.OrderBy(column => column));
        boundaries.Add(gridWidth);

        var roomDepthCenter = (-3f + (gridDepth - 3f)) / 2f;
        var roomDepthSize = gridDepth + 2 * ZoneDepthOverlapMargin;

        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];

            var leftPad = i == 0 && ShipSimRef.WestCorridorLength > 0
                ? CorridorSeamOverlapMargin
                : ZoneWidthOverlapMargin;
            var rightPad = i == boundaries.Count - 2 && ShipSimRef.EastCorridorLength > 0
                ? CorridorSeamOverlapMargin
                : ZoneWidthOverlapMargin;

            var left = start - 3f - leftPad;
            var right = end - 3f + rightPad;

            SpawnAtmosphereZone(
                shipRoot,
                tile: new Vector2I((start + end - 1) / 2, DoorwayRows[0]),
                centerX: (left + right) / 2f,
                sizeX: right - left,
                centerZ: roomDepthCenter,
                sizeZ: roomDepthSize);
        }

        var doorwayTop = DoorwayRows.Min() - 3f;
        var doorwayBottom = DoorwayRows.Max() - 3f + 1f;
        var corridorDepthCenter = (doorwayTop + doorwayBottom) / 2f;
        var corridorDepthSize = (doorwayBottom - doorwayTop) + 2 * ZoneDepthOverlapMargin;

        if (ShipSimRef.WestCorridorLength > 0)
        {
            var left = -ShipSimRef.WestCorridorLength - 3f - ZoneWidthOverlapMargin;
            var right = -1 - 3f + 1f + ZoneWidthOverlapMargin;

            SpawnAtmosphereZone(
                shipRoot,
                tile: new Vector2I(-1, DoorwayRows[0]),
                centerX: (left + right) / 2f,
                sizeX: right - left,
                centerZ: corridorDepthCenter,
                sizeZ: corridorDepthSize);
        }

        if (ShipSimRef.EastCorridorLength > 0)
        {
            var left = gridWidth - 3f - ZoneWidthOverlapMargin;
            var right = gridWidth + ShipSimRef.EastCorridorLength - 3f + ZoneWidthOverlapMargin;

            SpawnAtmosphereZone(
                shipRoot,
                tile: new Vector2I(gridWidth, DoorwayRows[0]),
                centerX: (left + right) / 2f,
                sizeX: right - left,
                centerZ: corridorDepthCenter,
                sizeZ: corridorDepthSize);
        }
    }

    private void SpawnAtmosphereZone(Node3D shipRoot, Vector2I tile, float centerX, float sizeX, float centerZ, float sizeZ)
    {
        var zone = new ShipAtmosphereZone
        {
            ShipSimRef = ShipSimRef,
            BuildTargetRef = this,
            Tile = tile,
            Transform = new Transform3D(Basis.Identity, new Vector3(centerX, WallCenterHeight, centerZ)),
        };
        shipRoot.AddChild(zone);

        zone.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(sizeX, WallHeight + 2 * ZoneHeightOverlapMargin, sizeZ) },
        });
    }

    private bool IsCorridorCell(CellCoord cell) =>
        ShipSimRef is not null && (cell.X < 0 || cell.X >= ShipSimRef.GridWidth);

    private float CeilingHeightFor(CellCoord cell) =>
        TallCeilingHeight > 0 && !IsCorridorCell(cell) ? TallCeilingHeight : CeilingPanelHeight;

    /// <summary>Claims a brand-new real Deck cell beyond the ship's existing footprint — genuine
    /// dynamic ship expansion, not just repairing an existing breached panel. The edge back to
    /// <paramref name="origin"/> becomes a normal open interior connection;
    /// <see cref="NormalizeBoundaryEdgesForCell"/> clears that edge's stale wall-breach flag and
    /// marks the new cell's other, still-open sides. Only the floor is claimed — the ceiling
    /// starts breached, so it needs its own Install Ceiling verb rather than coming for free.</summary>
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
    /// clear any wall-breach flag left over from before. If it's still not a real cell, mark that
    /// edge open. Checking generically (rather than tracking which direction was "the origin")
    /// means this works for save-replay too, where cells may have been extended in an arbitrary
    /// chain.</summary>
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
        // west/east boundary. Only the long sides need walling: the inner junction falls out of
        // the boundary-wall loop above, and the outer tip needs no wall since
        // AirlockDoorVerbTarget's own frame already caps it.
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
            // Full engine charge AND a full docked N2 tank ("1|n2_tank|1" — see
            // ThrusterVerbTarget.ApplySaveState's pipe-delimited format) — unlike a fresh
            // player-bought install (which starts empty), the Home Ship's two engines start
            // genuinely fueled.
            InstallThruster(ThrusterEdge1.A, ThrusterEdge1.B, savedState: "1|n2_tank|1");
            InstallThruster(ThrusterEdge2.A, ThrusterEdge2.B, savedState: "1|n2_tank|1");

            // Wire the whole default layout together — real, player-removable conduits through
            // the same InstallConduit a player's own wiring verb and a save replay use.
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
            UpdateHighlightGhostTransform();
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

            // The raw hit point's cell is normally the "inside" one, but a wall's collider has
            // real thickness — once an adjacent wall is gone, the player can end up standing in
            // that gap and aim at the next wall over from its outward face, flipping which side
            // the hit point falls on. Swap so _edgeA always ends up as real ship structure.
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
        UpdateHighlightGhostTransform();
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
        UpdateHighlightGhostTransform();
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

    // Only the two doorway edges (matching DoorwayRows) are InteriorDoorVerbTarget's to own — the
    // rest of this column is a real, solid wall this generic tool must still manage.
    private bool IsExcludedColumn(CellCoord a, CellCoord b) =>
        ExcludedEdgeColumn >= 0 &&
        DoorwayRows.Contains(a.Y) &&
        ((a.X == ExcludedEdgeColumn - 1 && b.X == ExcludedEdgeColumn) ||
         (b.X == ExcludedEdgeColumn - 1 && a.X == ExcludedEdgeColumn));

    /// <summary>Same tiering as <see cref="MaintenanceTier.PickVerb"/>, applied to whichever
    /// conduit fixture (if any) already occupies the given slot — null if nothing's placed
    /// there.</summary>
    private Verb? ConduitUpkeepVerb(ConduitSlot slot) =>
        _placedConduits.ContainsKey(slot) && ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == ConduitFixtureId(slot)) is { } fixture
            ? MaintenanceTier.PickVerb(fixture.Condition, MaintainConduitVerb, RepairConduitVerb)
            : null;

    /// <summary>The conduit fixture (if any) at whatever this target is currently aimed at — Tile
    /// aim means the floor-mounted slot, Edge aim means the wall-mounted one on the near side.
    /// Used by <see cref="Condition"/> to prefer a conduit's health over the underlying
    /// structural surface's.</summary>
    private Fixture? AimedConduitFixture()
    {
        var slot = _aimKind == AimKind.Edge
            ? new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot)
            : new ConduitSlot(_aimedTile, null);

        return ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ConduitFixtureId(slot));
    }

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
                var floorConduitSlot = new ConduitSlot(_aimedTile, null);
                var verbs = new List<Verb> { _placedConduits.ContainsKey(floorConduitSlot) ? RemoveConduitVerb : InstallConduitVerb };

                if (ConduitUpkeepVerb(floorConduitSlot) is { } floorConduitUpkeepVerb)
                {
                    verbs.Add(floorConduitUpkeepVerb);
                }

                // Floor panels are only a real, visualized thing on ships that opted into the
                // system (PanelMesh configured) — everything else keeps its fixed floor.
                if (PanelMesh is not null)
                {
                    var cell = new CellCoord(_aimedTile.X, _aimedTile.Y);
                    var floorPresent = !ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Floor);
                    verbs.Add(floorPresent ? RemoveFloorVerb : InstallFloorVerb);

                    if (floorPresent && MaintenanceTier.PickVerb(ShipSimRef.Deck.FloorHealth(cell), MaintainFloorVerb, RepairFloorVerb) is { } floorUpkeepVerb)
                    {
                        verbs.Add(floorUpkeepVerb);
                    }
                }

                return verbs;
            }

            case AimKind.Ceiling when PanelMesh is not null:
            {
                var cell = new CellCoord(_aimedTile.X, _aimedTile.Y);
                var ceilingPresent = !ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Ceiling);
                var verbs = new List<Verb> { ceilingPresent ? RemoveCeilingVerb : InstallCeilingVerb };

                if (ceilingPresent && MaintenanceTier.PickVerb(ShipSimRef.Deck.CeilingHealth(cell), MaintainCeilingVerb, RepairCeilingVerb) is { } ceilingUpkeepVerb)
                {
                    verbs.Add(ceilingUpkeepVerb);
                }

                return verbs;
            }

            case AimKind.Edge when !ShipSimRef.Deck.Cells.Contains(_edgeB):
            {
                // Boundary edge — Install repairs an existing breach, Remove deliberately creates
                // one, a real consequential choice. A wall-mounted conduit needs an actual hull
                // wall to mount on, so it's only offered while that hull is intact.
                var breached = ShipSimRef.Deck.IsWallEdgeBreached(_edgeA, _edgeB);
                var verbs = new List<Verb> { breached ? InstallWallVerb : RemoveWallVerb };
                if (breached)
                {
                    verbs.Add(ExtendFloorVerb);
                }
                else if (MaintenanceTier.PickVerb(ShipSimRef.Deck.WallHealth(_edgeA, _edgeB), MaintainWallVerb, RepairWallVerb) is { } boundaryWallUpkeepVerb)
                {
                    verbs.Add(boundaryWallUpkeepVerb);
                }

                if (EdgeConduitVerb(wallPresent: !breached) is { } boundaryConduitVerb)
                {
                    verbs.Add(boundaryConduitVerb);
                }

                if (ConduitUpkeepVerb(new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot)) is { } boundaryConduitUpkeepVerb)
                {
                    verbs.Add(boundaryConduitUpkeepVerb);
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

                if (sealed_ && MaintenanceTier.PickVerb(ShipSimRef.Deck.WallHealth(_edgeA, _edgeB), MaintainWallVerb, RepairWallVerb) is { } interiorWallUpkeepVerb)
                {
                    verbs.Add(interiorWallUpkeepVerb);
                }

                if (EdgeConduitVerb(wallPresent: sealed_) is { } interiorConduitVerb)
                {
                    verbs.Add(interiorConduitVerb);
                }

                if (ConduitUpkeepVerb(new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot)) is { } interiorConduitUpkeepVerb)
                {
                    verbs.Add(interiorConduitUpkeepVerb);
                }

                verbs.AddRange(MachineVerbsFor(wallPresent: sealed_));
                return verbs;
            }

            default:
                return [];
        }
    }

    // Aiming at an edge can mean "the wall itself" or "a conduit mounted on it," two different
    // objects sharing one aim point. Null when nothing is already placed AND there's no wall to
    // mount a new one on — removal of an already-placed conduit is always offered regardless,
    // only fresh installation requires a real wall to exist first.
    private Verb? EdgeConduitVerb(bool wallPresent)
    {
        var slot = new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot);
        if (_placedConduits.ContainsKey(slot))
        {
            return RemoveConduitVerb;
        }

        return wallPresent ? InstallConduitVerb : null;
    }

    private static IEnumerable<MachineType> AllMachineTypes => Machines.Keys;

    /// <summary>Same multi-verb-on-one-edge idea as <see cref="EdgeConduitVerb"/> — a machine
    /// already at this exact edge offers Uninstall/Scrap; an empty, wall-present edge offers
    /// Install for every machine type not already placed *anywhere* on the ship (at most one of
    /// each — see <see cref="_placedMachines"/>). Every Install verb is returned regardless of
    /// what's currently held; IsAffordable (Player.cs) narrows the visible cycle down to whichever
    /// item the player has in hand. Gated behind BatteryMesh so ships that never opted into this
    /// system never offer it.</summary>
    private IEnumerable<Verb> MachineVerbsFor(bool wallPresent)
    {
        if (BatteryMesh is null || !wallPresent)
        {
            yield break;
        }

        // Thrusters aren't MachineType-based and are keyed by the normalized edge — check this
        // edge for one first, same "occupied here means only Uninstall/Scrap, no stacking" rule
        // Battery/Switch/RechargeStation apply to each other below.
        if (_placedThrusters.ContainsKey(Deck.Normalize(_edgeA, _edgeB)))
        {
            yield return UninstallThrusterVerb;
            yield return ScrapThrusterVerb;
            yield break;
        }

        // Same rule as Thruster above — a storage unit claims its edge exclusively too, which is
        // what makes it genuinely "take up space" rather than stacking infinitely.
        if (_placedStorage.TryGetValue(Deck.Normalize(_edgeA, _edgeB), out var storageHere))
        {
            var suffix = Tr($"ITEM_{storageHere.ItemId.ToUpperInvariant()}");
            yield return UninstallStorageVerb with { DisplaySuffix = suffix };
            yield return ScrapStorageVerb with { DisplaySuffix = suffix };
            yield break;
        }

        var here = _placedMachines.FirstOrDefault(kv => kv.Value.EdgeA == _edgeA && kv.Value.EdgeB == _edgeB);
        if (here.Value.Node is not null)
        {
            foreach (var upkeepVerb in MachineMaintainRepairVerbs(here.Key))
            {
                yield return upkeepVerb;
            }

            yield return UninstallVerbFor(here.Key);
            yield return ScrapVerbFor(here.Key);
            yield break;
        }

        foreach (var type in AllMachineTypes.Where(t => !_placedMachines.ContainsKey(t)))
        {
            yield return InstallVerbFor(type);
        }

        // No "already placed anywhere" cap, unlike the loop above — that's the whole point of
        // thrusters over the single-instance MachineType system.
        yield return InstallThrusterVerb;

        // One generated Install verb per storage catalog item — a new storage tier needs only an
        // items.json entry, no new C# Verb.
        foreach (var itemId in ItemCatalog.StorageItemIds)
        {
            yield return new Verb($"install_storage:{itemId}", "VERB_INSTALL_STORAGE", DurationSeconds: 0.6f)
            {
                Requirements = [new ItemRequirement(itemId, 1)],
                DisplaySuffix = Tr($"ITEM_{itemId.ToUpperInvariant()}"),
            };
        }
    }

    private static Verb InstallVerbFor(MachineType type) => Definition(type).Install;

    private static Verb UninstallVerbFor(MachineType type) => Definition(type).Uninstall;

    private static Verb ScrapVerbFor(MachineType type) => Definition(type).Scrap;

    /// <summary>The Deck fixture id backing a machine type's wear/Condition — null for Battery,
    /// which deliberately has no upkeep verbs (see WearSystem/BatteryFixture).</summary>
    private static string? MachineFixtureIdFor(MachineType type) => Definition(type).FixtureId;

    /// <summary>Which machine type/pending action a machine verb id maps to — Action is null for
    /// any non-machine verb id, the signal ExecuteVerb/IsMachineVerb use to fall through.</summary>
    private static (MachineType Type, PendingAction? Action) ResolveMachineVerb(Verb verb)
    {
        foreach (var definition in Machines.Values)
        {
            if (verb.Id == definition.Install.Id)
            {
                return (definition.Type, PendingAction.InstallMachine);
            }

            if (verb.Id == definition.Uninstall.Id)
            {
                return (definition.Type, PendingAction.UninstallMachine);
            }

            if (verb.Id == definition.Scrap.Id)
            {
                return (definition.Type, PendingAction.ScrapMachine);
            }
        }

        return (default, null);
    }

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
    /// has no way to offer removal alongside its native verb (Recharge/Toggle/...).</summary>
    internal IReadOnlyList<Verb> MachineRemovalVerbs(MachineType type) =>
        _placedMachines.ContainsKey(type) ? [UninstallVerbFor(type), ScrapVerbFor(type)] : [];

    /// <summary>Same idea as <see cref="MachineRemovalVerbs"/> for thrusters — a thruster's own
    /// ThrusterVerbTarget exists only while installed, so this is unconditional.</summary>
    internal IReadOnlyList<Verb> ThrusterRemovalVerbs => [UninstallThrusterVerb, ScrapThrusterVerb];

    /// <summary>Same idea as <see cref="MachineRemovalVerbs"/>, for the Maintain/Repair pair.
    /// Empty for Battery or a machine not currently installed.</summary>
    internal IReadOnlyList<Verb> MachineMaintainRepairVerbs(MachineType type)
    {
        var definition = Definition(type);

        // FixtureId and Maintain/Repair are null together by construction, so this one check
        // covers all three.
        if (definition.FixtureId is not { } fixtureId)
        {
            return [];
        }

        if (ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == fixtureId) is not { } fixture)
        {
            return [];
        }

        return MaintenanceTier.PickVerb(fixture.Condition, definition.Maintain!, definition.Repair!) is { } verb ? [verb] : [];
    }

    /// <summary>Counterpart to <see cref="MachineRemovalVerbs"/> — a machine's own ExecuteVerb
    /// delegates here for any verb id it doesn't recognize as its own. Looks the edge up from
    /// _placedMachines directly rather than this body's own _edgeA/_edgeB, which reflect whatever
    /// *this* target was last aimed at, not necessarily this machine.</summary>
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

    /// <summary>Counterpart to <see cref="ExecuteMachineRemoval"/> for thrusters — takes the edge
    /// explicitly, since thrusters aren't MachineType-keyed.</summary>
    internal void ExecuteThrusterRemoval(CellCoord edgeA, CellCoord edgeB, Verb verb, PlayerInventory inventory)
    {
        if (_cycling || !_placedThrusters.ContainsKey(Deck.Normalize(edgeA, edgeB)))
        {
            return;
        }

        PendingAction action;
        if (verb.Id == UninstallThrusterVerb.Id)
        {
            action = PendingAction.UninstallThruster;
        }
        else if (verb.Id == ScrapThrusterVerb.Id)
        {
            action = PendingAction.ScrapThruster;
        }
        else
        {
            return;
        }

        _pendingAction = action;
        _pendingEdgeA = edgeA;
        _pendingEdgeB = edgeB;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    /// <summary>Same idea as <see cref="ThrusterRemovalVerbs"/> for storage — a shelf/bin's own
    /// StorageVerbTarget exists only while installed, so this is unconditional too.</summary>
    internal IReadOnlyList<Verb> StorageRemovalVerbs => [UninstallStorageVerb, ScrapStorageVerb];

    /// <summary>Counterpart to <see cref="ExecuteThrusterRemoval"/> for storage — same shape,
    /// takes the edge explicitly since storage (like Thruster) isn't MachineType-keyed.</summary>
    internal void ExecuteStorageRemoval(CellCoord edgeA, CellCoord edgeB, Verb verb, PlayerInventory inventory)
    {
        if (_cycling || !_placedStorage.TryGetValue(Deck.Normalize(edgeA, edgeB), out var placed))
        {
            return;
        }

        PendingAction action;
        if (verb.Id == UninstallStorageVerb.Id)
        {
            action = PendingAction.UninstallStorage;
        }
        else if (verb.Id == ScrapStorageVerb.Id)
        {
            action = PendingAction.ScrapStorage;
        }
        else
        {
            return;
        }

        _pendingAction = action;
        _pendingStorageItemId = placed.ItemId;
        _pendingEdgeA = edgeA;
        _pendingEdgeB = edgeB;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
    }

    /// <summary>Counterpart to <see cref="MachineMaintainRepairVerbs"/> — same delegation shape as
    /// <see cref="ExecuteMachineRemoval"/>.</summary>
    internal void ExecuteMachineMaintainRepair(MachineType type, Verb verb, PlayerInventory inventory)
    {
        var definition = Definition(type);
        if (_cycling || !_placedMachines.ContainsKey(type) || definition.FixtureId is null)
        {
            return;
        }

        PendingAction action;
        if (verb.Id == definition.Maintain!.Id)
        {
            action = PendingAction.MaintainMachine;
        }
        else if (verb.Id == definition.Repair!.Id)
        {
            action = PendingAction.RepairMachine;
        }
        else
        {
            return;
        }

        _pendingAction = action;
        _pendingMachineType = type;
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

        if (verb.Id == InstallThrusterVerb.Id)
        {
            _pendingAction = PendingAction.InstallThruster;
            _pendingEdgeA = _edgeA;
            _pendingEdgeB = _edgeB;
            _pendingInventory = inventory;
            _cycling = true;
            _cycleTimer!.Start();
            return;
        }

        if (verb.Id == UninstallThrusterVerb.Id || verb.Id == ScrapThrusterVerb.Id)
        {
            ExecuteThrusterRemoval(_edgeA, _edgeB, verb, inventory);
            return;
        }

        if (verb.Id.StartsWith("install_storage:"))
        {
            _pendingAction = PendingAction.InstallStorage;
            _pendingStorageItemId = verb.Id["install_storage:".Length..];
            _pendingEdgeA = _edgeA;
            _pendingEdgeB = _edgeB;
            _pendingInventory = inventory;
            _cycling = true;
            _cycleTimer!.Start();
            return;
        }

        if (verb.Id == UninstallStorageVerb.Id || verb.Id == ScrapStorageVerb.Id)
        {
            ExecuteStorageRemoval(_edgeA, _edgeB, verb, inventory);
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

        if (verb.Id == MaintainFloorVerb.Id || verb.Id == RepairFloorVerb.Id)
        {
            ExecuteStructuralUpkeepTileVerb(verb.Id == MaintainFloorVerb.Id ? PendingAction.MaintainFloor : PendingAction.RepairFloor, inventory);
            return;
        }

        if (verb.Id == MaintainCeilingVerb.Id || verb.Id == RepairCeilingVerb.Id)
        {
            ExecuteStructuralUpkeepTileVerb(verb.Id == MaintainCeilingVerb.Id ? PendingAction.MaintainCeiling : PendingAction.RepairCeiling, inventory);
            return;
        }

        if (verb.Id == MaintainWallVerb.Id || verb.Id == RepairWallVerb.Id)
        {
            _pendingAction = verb.Id == MaintainWallVerb.Id ? PendingAction.MaintainWall : PendingAction.RepairWall;
            _pendingEdgeA = _edgeA;
            _pendingEdgeB = _edgeB;
            _pendingInventory = inventory;
            _cycling = true;
            _cycleTimer!.Start();
            return;
        }

        if (verb.Id == MaintainConduitVerb.Id || verb.Id == RepairConduitVerb.Id)
        {
            var slot = _aimKind == AimKind.Edge
                ? new ConduitSlot(new Vector2I(_edgeA.X, _edgeA.Y), _edgeB, _aimedWallSlot)
                : new ConduitSlot(_aimedTile, null);

            _pendingAction = verb.Id == MaintainConduitVerb.Id ? PendingAction.MaintainConduit : PendingAction.RepairConduit;
            _pendingSlot = slot;
            _pendingInventory = inventory;
            _cycling = true;
            _cycleTimer!.Start();
            return;
        }

        if (_aimKind == AimKind.Edge)
        {
            ExecuteWallVerb(verb, inventory);
        }
    }

    /// <summary>Shared by the floor/ceiling Maintain and Repair verbs — both just record the
    /// aimed tile and the chosen action, differing only in PendingAction.</summary>
    private void ExecuteStructuralUpkeepTileVerb(PendingAction action, PlayerInventory inventory)
    {
        _pendingAction = action;
        _pendingTile = _aimedTile;
        _pendingInventory = inventory;
        _cycling = true;
        _cycleTimer!.Start();
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
            case PendingAction.InstallThruster:
                InstallThruster(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.UninstallThruster:
                RemoveThruster(_pendingEdgeA, _pendingEdgeB, inventory);
                AddOrDrop(inventory, "thruster", 1);
                break;
            case PendingAction.ScrapThruster:
                RemoveThruster(_pendingEdgeA, _pendingEdgeB, inventory);
                AddOrDrop(inventory, "scrap_metal", ThrusterScrapYield);
                break;
            case PendingAction.InstallStorage:
                InstallStorage(_pendingStorageItemId, _pendingEdgeA, _pendingEdgeB, savedState: null);
                break;
            case PendingAction.UninstallStorage:
                RemoveStorage(_pendingEdgeA, _pendingEdgeB, inventory);
                AddOrDrop(inventory, _pendingStorageItemId, 1);
                break;
            case PendingAction.ScrapStorage:
                RemoveStorage(_pendingEdgeA, _pendingEdgeB, inventory);
                AddOrDrop(inventory, "scrap_metal", Mathf.Max(1, ItemCatalog.StorageSlotCount(_pendingStorageItemId) / 3));
                break;
            case PendingAction.MaintainFloor:
            case PendingAction.RepairFloor:
                ShipSimRef!.Deck.RepairFloor(new CellCoord(_pendingTile.X, _pendingTile.Y));
                break;
            case PendingAction.MaintainCeiling:
            case PendingAction.RepairCeiling:
                ShipSimRef!.Deck.RepairCeiling(new CellCoord(_pendingTile.X, _pendingTile.Y));
                break;
            case PendingAction.MaintainWall:
            case PendingAction.RepairWall:
                ShipSimRef!.Deck.RepairWall(_pendingEdgeA, _pendingEdgeB);
                break;
            case PendingAction.MaintainConduit:
            case PendingAction.RepairConduit:
                if (ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == ConduitFixtureId(_pendingSlot)) is { } conduitFixture)
                {
                    conduitFixture.Condition = 1f;
                }

                break;
            case PendingAction.MaintainMachine:
            case PendingAction.RepairMachine:
                if (MachineFixtureIdFor(_pendingMachineType) is { } machineFixtureId &&
                    ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == machineFixtureId) is { } machineFixture)
                {
                    machineFixture.Condition = 1f;
                }

                break;
        }
    }

    /// <summary>Adds a refund/scrap-yield item to the player's inventory, dropping whatever
    /// doesn't fit as a world pickup right where this action happened instead of losing it.</summary>
    private void AddOrDrop(PlayerInventory? inventory, string itemId, int count)
    {
        if (inventory is null)
        {
            return;
        }

        var added = inventory.Add(itemId, count);
        if (added < count)
        {
            InventoryOverflow.DropAt(this, itemId, count - added);
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
    /// currently carry any fixture — same adjacency PowerSystem uses to decide connectivity, so
    /// the shape never lies about what's actually wired. Zero neighbors falls back to the plain
    /// lone-conduit box; each connected direction gets its own short arm meeting at the tile
    /// center, so 2 opposite directions read as a straight run, 2 adjacent as a corner, 3 as a T,
    /// 4 as a cross — all built from the same one arm mesh.</summary>
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

    /// <summary>Same idea as <see cref="BuildFloorConduitVisual"/>, adapted to a wall mount:
    /// another conduit further along the *same* wall (an along-wall arm), a floor conduit on the
    /// same tile (a real 2-segment connector — horizontal reach then vertical drop), or a
    /// *different* wall's conduit on the same tile (a short inward stub only — reaching it would
    /// mean wrapping the room's corner, extra geometry this pass skips). Floor takes priority if
    /// both share the tile. Nothing connected falls back to the plain lone wall-conduit box.</summary>
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
            // A real connector reaching the floor conduit's tile-center hub, measured against its
            // actual position. Routed down the wall/corner FIRST and only jogging out to the
            // floor conduit's hub once at floor height — a real wire runs down a corner then
            // along the floor, it doesn't jut into open air and drop through nothing.
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
        // companion that drives its inward "connects into the room" stub.
        foreach (var other in _placedConduits.Keys.Where(s => s.OnWall && s.Tile == slot.Tile && s != slot).ToList())
        {
            RefreshWallConduitVisual(other);
        }
    }

    private static float SlotHeight(int slot) => ShipGeometry.SlotHeight(slot);

    /// <summary>Same edge position/rotation a wall segment would use, nudged toward whichever tile
    /// the mount belongs to (reads as mounted on that tile's wall face instead of embedded in the
    /// wall) and raised/lowered to the given height. Shared by conduits and machines — the only
    /// difference between mounting a wire and mounting a battery is how far up and out it sits.</summary>
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
    /// plus its charge IndicatorLight), mounts it at the given edge, and registers its fixture
    /// with ShipSim. <paramref name="savedState"/>, if given, is applied directly to the
    /// freshly-built instance — no group-scan save mechanism is involved, since a dynamically
    /// spawned machine is never in the "saveable" group.</summary>
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
    /// <paramref name="savedState"/> is the switch's on/off bool, stringified.</summary>
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
    /// Recharge Station has no extra state, so <paramref name="savedState"/> is unused (kept for
    /// a uniform three-way call signature).</summary>
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

    // Placeholder/tunable — roughly matches Switch/RechargeStation's own ScrapYieldFor tier.
    private const int ThrusterScrapYield = 2;

    /// <summary>Unlike Battery/Switch/RechargeStation's single fixed constant id, each installed
    /// thruster needs its own — derived from its normalized mounting edge so it's stable
    /// regardless of which side of the wall it's read from.</summary>
    private static string ThrusterFixtureId(CellCoord edgeA, CellCoord edgeB)
    {
        var (a, b) = Deck.Normalize(edgeA, edgeB);
        return $"thruster_{a.X}_{a.Y}_{b.X}_{b.Y}";
    }

    /// <summary>Same shape as <see cref="InstallBattery"/>/etc, but not MachineType-based — see
    /// <see cref="_placedThrusters"/>. <paramref name="savedState"/> is the thruster's own N2
    /// charge fraction, stringified (see ThrusterVerbTarget.ApplySaveState).</summary>
    private void InstallThruster(CellCoord edgeA, CellCoord edgeB, string? savedState = null)
    {
        var nearTile = new Vector2I(edgeA.X, edgeA.Y);
        var (position, rotation) = WallMountTransform(edgeA, edgeB, nearTile, ThrusterHeight, ThrusterRoomOffset);
        var fixtureId = ThrusterFixtureId(edgeA, edgeB);

        var node = new ThrusterVerbTarget
        {
            ShipSimRef = ShipSimRef,
            BuildTarget = this,
            FixtureId = fixtureId,
            EdgeA = edgeA,
            EdgeB = edgeB,
        };
        AddChild(node);
        node.Position = position;
        node.RotationDegrees = rotation;

        var mesh = new MeshInstance3D { Mesh = ThrusterMesh };
        mesh.SetSurfaceOverrideMaterial(0, ThrusterMaterial);
        node.AddChild(mesh);

        node.AddChild(new CollisionShape3D { Shape = ThrusterShape });

        ShipSimRef!.InstallThruster(fixtureId, edgeA, FixtureSurface.WallInner);

        if (savedState is not null)
        {
            node.ApplySaveState(savedState);
        }

        _placedThrusters[Deck.Normalize(edgeA, edgeB)] = node;
    }

    private void RemoveThruster(CellCoord edgeA, CellCoord edgeB, PlayerInventory? inventory)
    {
        if (!_placedThrusters.Remove(Deck.Normalize(edgeA, edgeB), out var node))
        {
            return;
        }

        // Any N2 tank still docked inside goes back to the player/world — it's real player
        // property, not scenery.
        if (node.Contents.Slots[0] is { } tank)
        {
            AddOrDrop(inventory, tank.ItemId, tank.Count);
        }

        ShipSimRef?.RemoveThruster(node.FixtureId);
        node.QueueFree();
    }

    /// <summary>Same shape as <see cref="ThrusterFixtureId"/>, distinct prefix — one id per
    /// installed shelf/bin, derived from its normalized mounting edge.</summary>
    private static string StorageFixtureId(CellCoord edgeA, CellCoord edgeB)
    {
        var (a, b) = Deck.Normalize(edgeA, edgeB);
        return $"storage_{a.X}_{a.Y}_{b.X}_{b.Y}";
    }

    /// <summary>Same shape as <see cref="InstallThruster"/>, but <paramref name="savedState"/> —
    /// when present — sizes <see cref="StorageVerbTarget.Contents"/> from its own segment count
    /// instead of consulting <see cref="ItemCatalog.StorageSlotCount"/>: a reload restores
    /// exactly the slots that existed at save time, even if items.json later changes that tier's
    /// capacity. A fresh, verb-triggered install (savedState null) does consult ItemCatalog, since
    /// there's no saved shape to preserve yet.</summary>
    private void InstallStorage(string itemId, CellCoord edgeA, CellCoord edgeB, string? savedState = null)
    {
        var nearTile = new Vector2I(edgeA.X, edgeA.Y);
        var (position, rotation) = WallMountTransform(edgeA, edgeB, nearTile, StorageHeight, StorageRoomOffset);
        var fixtureId = StorageFixtureId(edgeA, edgeB);

        var node = new StorageVerbTarget
        {
            ShipSimRef = ShipSimRef,
            BuildTarget = this,
            FixtureId = fixtureId,
            EdgeA = edgeA,
            EdgeB = edgeB,
            ItemId = itemId,
            Contents = new SlotContainer(savedState is not null ? savedState.Split(';').Length : ItemCatalog.StorageSlotCount(itemId)),
        };
        AddChild(node);
        node.Position = position;
        node.RotationDegrees = rotation;

        var mesh = new MeshInstance3D { Mesh = StorageMesh };
        mesh.SetSurfaceOverrideMaterial(0, StorageMaterial);
        node.AddChild(mesh);

        node.AddChild(new CollisionShape3D { Shape = StorageShape });

        ShipSimRef!.InstallStorage(fixtureId, edgeA, FixtureSurface.WallInner);

        if (savedState is not null)
        {
            node.ApplySaveState(savedState);
        }

        _placedStorage[Deck.Normalize(edgeA, edgeB)] = node;
    }

    private void RemoveStorage(CellCoord edgeA, CellCoord edgeB, PlayerInventory? inventory)
    {
        if (!_placedStorage.Remove(Deck.Normalize(edgeA, edgeB), out var node))
        {
            return;
        }

        // Every slot still holding something goes back to the player/world.
        foreach (var slot in node.Contents.Slots)
        {
            if (slot is { } item)
            {
                AddOrDrop(inventory, item.ItemId, item.Count);
            }
        }

        ShipSimRef?.RemoveStorage(node.FixtureId);
        node.QueueFree();
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

    private static string ItemIdFor(MachineType type) => Definition(type).ItemId;

    private static int ScrapYieldFor(MachineType type) => Definition(type).ScrapYield;

    /// <summary>Ghost shape/position depends on both where you're aiming AND which install verb is
    /// currently highlighted — e.g. "install a conduit" and "build a floor panel" are different
    /// objects sharing the same tile aim point. Always shows the plain lone/panel-box shape
    /// rather than a live connection-aware preview — a rough "here's where" indicator.</summary>
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
                // Previews the new cell itself (across the edge, at _edgeB) — that's where the
                // floor panel will actually land.
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

    /// <summary>Keeps the dedicated highlight mesh (see HighlightVisual) tracking the current aim
    /// point — unconditional on _previewVerb/install-preview state, unlike UpdateGhostTransform,
    /// since scan mode needs a silhouette regardless of which verb is selected.</summary>
    private void UpdateHighlightGhostTransform()
    {
        switch (_aimKind)
        {
            case AimKind.Tile:
                _highlightGhost!.Mesh = PanelMesh ?? ConduitMesh;
                _highlightGhost.RotationDegrees = Vector3.Zero;
                _highlightGhost.Position = ToLocal(TileWorldPosition(_aimedTile, FloorPanelHeight));
                break;

            case AimKind.Ceiling:
                _highlightGhost!.Mesh = PanelMesh;
                _highlightGhost.RotationDegrees = Vector3.Zero;
                _highlightGhost.Position = ToLocal(TileWorldPosition(_aimedTile, CeilingPanelHeight));
                break;

            case AimKind.Edge:
            {
                // EdgeMargin's band is deliberately wide, but that means "aim resolved to Edge"
                // does NOT imply a wall actually exists there yet — outlining a phantom wall over
                // open floor reads as wrong, so this falls back to highlighting the floor tile.
                var wallPresent = ShipSimRef!.Deck.Cells.Contains(_edgeB)
                    ? ShipSimRef.Deck.IsEdgeSealed(_edgeA, _edgeB)
                    : !ShipSimRef.Deck.IsWallEdgeBreached(_edgeA, _edgeB);

                if (wallPresent)
                {
                    _highlightGhost!.Mesh = WallSegmentMesh;
                    var (position, rotationDegrees) = EdgeTransform(_edgeA, _edgeB);
                    _highlightGhost.Position = position;
                    _highlightGhost.RotationDegrees = rotationDegrees;
                }
                else
                {
                    _highlightGhost!.Mesh = PanelMesh ?? ConduitMesh;
                    _highlightGhost.RotationDegrees = Vector3.Zero;
                    _highlightGhost.Position = ToLocal(TileWorldPosition(_aimedTile, FloorPanelHeight));
                }

                break;
            }
        }
    }

    /// <summary>Local (to this Floor body) position + rotation for a 1-tile wall segment on the
    /// given edge — works uniformly for interior edges and boundary edges (where the "far" cell
    /// is off-grid), since the midpoint math is the same either way.</summary>
    private static Vector3 EdgeShipLocalPosition(CellCoord a, CellCoord b) =>
        ShipGeometry.EdgeShipLocal(a, b, WallCenterHeight);

    private (Vector3 Position, Vector3 RotationDegrees) EdgeTransform(CellCoord a, CellCoord b) =>
        (ToLocal(EdgeWorldPosition(a, b)), ShipGeometry.EdgeRotationDegrees(a, b));

    private Vector3 EdgeWorldPosition(CellCoord a, CellCoord b) =>
        ShipSpace.ToGlobal(EdgeShipLocalPosition(a, b));

    private Vector3 TileWorldPosition(Vector2I tile, float height) =>
        ShipSpace.ToGlobal(ShipGeometry.TileShipLocal(tile, height));

    /// <summary>This ship's spatial root — every grid-to-world conversion goes through it, so a
    /// ship instanced anywhere places its geometry relative to itself rather than to the world
    /// origin. Falls back to this node's own parent for a scene that hasn't wired ShipRoot.</summary>
    private Node3D ShipSpace => ShipRoot ?? GetParent<Node3D>();

    /// <summary>World positions of every currently-open floor/ceiling/wall breach on this ship —
    /// the decompression-pull hazard's read of Deck.HullBreaches/WallEdgeBreaches.</summary>
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

            // Wall, per-CELL rather than per-edge — a direct "this whole cell is exposed to
            // vacuum" event, e.g. AirlockDoorVerbTarget venting its adjacent cell while undocked.
            // Distinct from the per-edge wall breaches below (a player-removed wall segment).
            if (ShipSimRef.Deck.IsHullBreached(cell, StructuralSurface.Wall))
            {
                yield return TileWorldPosition(tile, WallCenterHeight);
            }
        }

        // A breached/removed wall SEGMENT is tracked per-edge (Deck.WallEdgeBreaches), not the
        // per-cell breach set above, so it needs its own pass.
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

            var conduitHealth = ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == ConduitFixtureId(slot))?.Condition ?? 1f;
            if (conduitHealth < 1f)
            {
                data.ConduitConditions[ConduitFixtureId(slot)] = conduitHealth;
            }
        }

        foreach (var (a, b) in _placedWalls.Keys)
        {
            data.Walls.Add(new EdgeCoord(a.X, a.Y, b.X, b.Y));

            var wallHealth = ShipSimRef!.Deck.WallHealth(a, b);
            if (wallHealth < 1f)
            {
                data.WallHealthEntries.Add(new EdgeHealthCoord(a.X, a.Y, b.X, b.Y, wallHealth));
            }
        }

        foreach (var cell in _floorPanels.Keys)
        {
            if (ShipSimRef!.Deck.IsHullBreached(cell, StructuralSurface.Floor))
            {
                data.FloorBreaches.Add(new TileCoord(cell.X, cell.Y));
            }

            var floorHealth = ShipSimRef.Deck.FloorHealth(cell);
            if (floorHealth < 1f)
            {
                data.FloorHealthEntries.Add(new TileHealthCoord(cell.X, cell.Y, floorHealth));
            }
        }

        foreach (var cell in _ceilingPanels.Keys)
        {
            if (ShipSimRef!.Deck.IsHullBreached(cell, StructuralSurface.Ceiling))
            {
                data.CeilingBreaches.Add(new TileCoord(cell.X, cell.Y));
            }

            var ceilingHealth = ShipSimRef.Deck.CeilingHealth(cell);
            if (ceilingHealth < 1f)
            {
                data.CeilingHealthEntries.Add(new TileHealthCoord(cell.X, cell.Y, ceilingHealth));
            }
        }

        foreach (var (type, placed) in _placedMachines)
        {
            var machineHealth = MachineFixtureIdFor(type) is { } machineFixtureId
                ? ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == machineFixtureId)?.Condition ?? 1f
                : 1f;
            data.Machines.Add(new MachineCoord(ItemIdFor(type), placed.EdgeA.X, placed.EdgeA.Y, placed.EdgeB.X, placed.EdgeB.Y, MachineStateOf(type, placed.Node), machineHealth));
        }

        // Thrusters share the same flat Machines list (Type "thruster") rather than a dedicated
        // field — MachineCoord already supports arbitrary rows and multiple entries of the same
        // Type. Condition is left at its default (1f): ThrusterFixture is excluded from wear,
        // same as Battery, so there's nothing but charge (State) to round-trip.
        foreach (var (edge, node) in _placedThrusters)
        {
            data.Machines.Add(new MachineCoord("thruster", edge.Item1.X, edge.Item1.Y, edge.Item2.X, edge.Item2.Y, node.GetSaveState()));
        }

        // Type is "storage:" + the storage item's own id ("storage:small_bin", etc.) — the
        // prefix lets ApplyBuildState recognize a storage row on sight without ever consulting
        // ItemCatalog. Condition is left at its default (1f) — storage has no charge/wear concept.
        foreach (var (edge, node) in _placedStorage)
        {
            data.Machines.Add(new MachineCoord($"storage:{node.ItemId}", edge.Item1.X, edge.Item1.Y, edge.Item2.X, edge.Item2.Y, node.GetSaveState()));
        }

        foreach (var cell in _extendedCells)
        {
            data.ExtendedCells.Add(new TileCoord(cell.X, cell.Y));
        }

        return data;
    }

    /// <summary>Reverse of <see cref="ItemIdFor"/> — null for anything that isn't one of the
    /// single-instance machines. Derived from the same table, so the two can't drift apart.</summary>
    private static MachineType? MachineTypeFromItemId(string itemId) =>
        Machines.Values.FirstOrDefault(d => d.ItemId == itemId)?.Type;

    /// <summary>Replays a save's tiles/edges through the same helpers Install/BuildWall use —
    /// already inventory-free at this level, so restoring a save never re-charges scrap_metal/
    /// wall_panel. Clears all current conduit/wall/floor/ceiling state first: the ship's own
    /// default layout is itself now removable, so a loaded save must be authoritative rather
    /// than layered on top of whatever startup already built.</summary>
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

        foreach (var (fixtureId, health) in state.ConduitConditions)
        {
            if (ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == fixtureId) is { } conduitFixture)
            {
                conduitFixture.Condition = health;
            }
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

        foreach (var entry in state.WallHealthEntries)
        {
            ShipSimRef!.Deck.SetWallHealth(new CellCoord(entry.AX, entry.AY), new CellCoord(entry.BX, entry.BY), entry.Health);
        }

        // ClearAllBuildState just breached every floor/ceiling tile as its "removed" baseline —
        // repair them all back to intact before selectively re-breaching only the ones the save
        // actually recorded as open. Skipping this would drop the player through the entire
        // floor and ceiling on every load except the tiles breached when the save was made.
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

        foreach (var entry in state.FloorHealthEntries)
        {
            ShipSimRef!.Deck.SetFloorHealth(new CellCoord(entry.X, entry.Y), entry.Health);
        }

        foreach (var entry in state.CeilingHealthEntries)
        {
            ShipSimRef!.Deck.SetCeilingHealth(new CellCoord(entry.X, entry.Y), entry.Health);
        }

        foreach (var machine in state.Machines)
        {
            if (machine.Type == "thruster")
            {
                InstallThruster(new CellCoord(machine.EdgeAX, machine.EdgeAY), new CellCoord(machine.EdgeBX, machine.EdgeBY), machine.State);
                continue;
            }

            if (machine.Type.StartsWith("storage:"))
            {
                var storageItemId = machine.Type["storage:".Length..];
                InstallStorage(storageItemId, new CellCoord(machine.EdgeAX, machine.EdgeAY), new CellCoord(machine.EdgeBX, machine.EdgeBY), machine.State);
                continue;
            }

            if (MachineTypeFromItemId(machine.Type) is not { } type)
            {
                GD.PushWarning($"[ShipBuildTarget] Save references unknown machine type '{machine.Type}' — skipping.");
                continue;
            }

            InstallMachine(type, new CellCoord(machine.EdgeAX, machine.EdgeAY), new CellCoord(machine.EdgeBX, machine.EdgeBY), machine.State);

            if (MachineFixtureIdFor(type) is { } machineFixtureId &&
                ShipSimRef!.Deck.Fixtures.FirstOrDefault(f => f.Id == machineFixtureId) is { } machineFixture)
            {
                machineFixture.Condition = machine.Condition;
            }
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

        foreach (var (a, b) in _placedThrusters.Keys.ToList())
        {
            RemoveThruster(a, b, inventory: null);
        }

        foreach (var (a, b) in _placedStorage.Keys.ToList())
        {
            RemoveStorage(a, b, inventory: null);
        }

        // A load that doesn't include a previously-extended cell must actually remove it — Load()
        // applies state onto the live scene, not a fresh reload, so a stale extension from
        // earlier in the same session would otherwise just linger.
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
