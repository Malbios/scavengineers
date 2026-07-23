using System;
using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Fleet;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Scripts.Ship;

/// <summary>Owns and ticks a real Scavengineers.Sim <see cref="ShipModel.Deck"/> for a greybox
/// ship: a full 1m-tile grid matching the room floor plan built in the scene (rooms are
/// <see cref="GridWidth"/> tiles wide, split into columns by <see cref="RoomSplitColumns"/>, both
/// <see cref="GridDepth"/> deep), optionally hosting a hull breach (atmosphere) and/or a
/// battery/switch/recharge-station power grid, toggled per scene via the exported flags.</summary>
public partial class ShipSim : Node, IShipLayoutSaveable, IShipStateSaveable
{
    // Public so ShipBuildTarget.SeedDefaultShipLayout can size its own corridor-wall seeding off
    // the same single source of truth instead of re-hardcoding it.
    public const int GridDepth = 6;

    // Room 1 = i 0-5, Room 2 = i 6-11 by default (matches MidWallA/B at local x=3). Each split
    // column seals every row except DoorwayRows, which stays open as that boundary's doorway.
    // Home Ship and Station keep the single default split; the Derelict overrides both this and
    // GridWidth in the scene to add a third room.
    [Export]
    public int GridWidth { get; set; } = 12;

    [Export]
    public int[] RoomSplitColumns { get; set; } = [6];

    /// <summary>Extra 2-tile-wide (DoorwayRows-only) strip of cells attached to the west/east
    /// boundary — real Deck.Cells, not a separate grid, so the floor/wall/ceiling panel systems
    /// apply automatically. 0 by default; only the Home Ship's airlock corridors use these today.</summary>
    [Export]
    public int WestCorridorLength { get; set; }

    [Export]
    public int EastCorridorLength { get; set; }

    // Public so AirlockDoorVerbTarget's own configurable edge-seal (station destination doors)
    // can reuse this instead of re-hardcoding [2, 3].
    public static readonly int[] DoorwayRows = [2, 3];

    // Every device below is added unwired — the player must run their own conduits via
    // ShipBuildTarget's free-form placement to connect any of them to the battery. These stay
    // static/shared, not per-layout data: they only ever fire under HasPowerGrid, which today
    // only the Home Ship sets.
    private static readonly CellCoord TravelConsoleCell = new(0, 0);
    private static readonly CellCoord InteriorDoorCell = new(5, 2);
    private static readonly CellCoord StationAirlockCell = new(0, 2);
    private static readonly CellCoord DerelictAirlockCell = new(11, 2);

    // Nominal placeholder cell for the Station's own destination-side airlock door's wear/power
    // fixture — shared by every Station instance, same convention as InteriorDoorCell.
    private static readonly CellCoord StationDestinationAirlockCell = new(5, 3);
    private static readonly CellCoord BunkCell = new(0, 1);

    /// <summary>The Derelict's starting hull breaches — a real gap cut into the wall mesh at
    /// these edges, paired with each edge's "outside" neighbor since a wall breach is per-edge
    /// (Deck.BreachWallEdge), not per-cell. Instance property so <see cref="ApplyLayout"/> can
    /// give different derelicts different breach positions; the default is today's original
    /// value, so a ship with no <see cref="LayoutId"/> is unaffected.</summary>
    public (CellCoord Cell, CellCoord Outside)[] InitialBreaches { get; set; } =
        [(new(6, 5), new(6, 6)), (new(3, 0), new(3, -1))];

    /// <summary>A minimal, switch-less power source just to energize one already-damaged conduit
    /// (the fire hazard), kept separate from HasPowerGrid's home-ship-shaped generator/switch/
    /// recharge chain.</summary>
    public CellCoord FireGeneratorCell { get; set; } = new(1, 4);

    public CellCoord DamagedConduitCell { get; set; } = new(1, 3);

    /// <summary>Opt-in id into <see cref="ShipLayoutCatalog"/> — empty (default) means this ship
    /// keeps its own exported fields/defaults. Only Derelict-style ships set this.</summary>
    [Export]
    public string LayoutId { get; set; } = "";

    /// <summary>0 for a ship's primary (ground-floor) deck — every ship before multi-deck support
    /// existed. Nonzero means "resolve my shape from <see cref="PrimaryDeckRef"/>'s
    /// <see cref="SecondDeckLayout"/> instead of my own LayoutId/ProcedurallyGenerate." Only 0/1
    /// are meaningful today (depth capped at one extra deck).</summary>
    [Export]
    public int DeckIndex { get; set; }

    /// <summary>Only meaningful when <see cref="DeckIndex"/> is nonzero — the same site's
    /// ground-floor ShipSim, whose resolved <see cref="SecondDeckLayout"/>/<see cref="LadderCell"/>
    /// this deck reads instead of resolving anything itself.</summary>
    [Export]
    public ShipSim? PrimaryDeckRef { get; set; }

    /// <summary>This ship's own second deck's layout, if any — set from
    /// ShipLayoutCatalog.ShipLayoutDefinition.SecondDeck by <see cref="ApplyLayout"/>. Null means
    /// single-deck. Read by a DeckIndex=1 ShipSim (via <see cref="PrimaryDeckRef"/>) to resolve
    /// its own shape — see <see cref="_Ready"/>.</summary>
    public ShipLayoutCatalog.ShipLayoutDefinition? SecondDeckLayout { get; private set; }

    /// <summary>The shared X/Z tile (only Y differs between decks) where a ladder connects this
    /// deck to its second deck — null for a single-deck ship. Set from the layout's LadderCell by
    /// <see cref="ApplyLayout"/> for a primary deck; a DeckIndex=1 deck instead copies it from
    /// <see cref="PrimaryDeckRef"/> in <see cref="_Ready"/>.</summary>
    public CellCoord? LadderCell { get; private set; }

    /// <summary>Rolls a random <see cref="ShipLayoutGenerator"/> layout instead of reading
    /// <see cref="LayoutId"/> from the catalog — wins if both are somehow set. The resolved
    /// <see cref="LayoutSeed"/> is read from (and persisted to) the save file directly, since it
    /// can't go through the normal ApplySaveState callback.</summary>
    [Export]
    public bool ProcedurallyGenerate { get; set; }

    /// <summary>Only meaningful alongside <see cref="ProcedurallyGenerate"/>.</summary>
    [Export]
    public string SaveId { get; set; } = "";

    /// <summary>Test-only seam: points the seed read at a temp file instead of the player's real
    /// save data. Every production ship leaves this unset and reads
    /// <see cref="SaveManager.DefaultSavePath"/>.</summary>
    [Export]
    public string? SavePathOverride { get; set; }

    /// <summary>This ship's resolved procedural-generation seed — null unless
    /// <see cref="ProcedurallyGenerate"/>. See IShipLayoutSaveable.</summary>
    public int? LayoutSeed { get; private set; }

    /// <summary>This ship's own procedurally-generated loot list, if any — empty unless
    /// <see cref="ApplyGeneratedLayout"/> was called. Spawned by ShipBuildTarget.SpawnGeneratedLoot,
    /// which already owns the tile-to-world conversion and the generic dropped-item mesh/shape.</summary>
    public IReadOnlyList<LootSpawn> LootSpawns { get; private set; } = [];

    public const string BatteryFixtureId = "battery";
    public const string SwitchFixtureId = "switch";
    public const string RechargeFixtureId = "recharge_station";
    public const string FireGeneratorFixtureId = "fire_generator";
    public const string DamagedConduitFixtureId = "damaged_conduit";
    public const string TravelConsoleFixtureId = "travel_console_power";
    public const string InteriorDoorFixtureId = "interior_door_power";
    public const string StationAirlockFixtureId = "station_airlock_power";
    public const string DerelictAirlockFixtureId = "derelict_airlock_power";

    // Shared by every Station's own destination-side airlock door, same convention as
    // InteriorDoorFixtureId.
    public const string StationDestinationAirlockFixtureId = "station_destination_airlock_power";
    public const string BunkFixtureId = "bunk";

    // Placeholder/tunable — one power cell (see VendorVerbTarget's economy) restores
    // this fraction of a full charge.
    public const float PowerCellRechargeAmount = 0.5f;

    // Placeholder/tunable power budget — a battery can only actually sustain this much total
    // draw at once (see DemandedPower/IsOverloaded); every consumer's own baseline is the same
    // flat idle draw, with the three "working" devices spiking to their own active draw only
    // while genuinely in use (Recharge Station recharging, Travel Console/Thrusters traveling).
    public const float BatteryCapacity = 20f;
    public const float IdleDraw = 1f;
    public const float RechargeStationActiveDraw = 10f;
    public const float TravelConsoleActiveDraw = 8f;

    // Per-thruster, not shared — N installed thrusters draw N times this while traveling. Low
    // enough that a normal loadout never trips IsOverloaded by itself; stacking well beyond that
    // (or recharging mid-flight) still can, keeping the ship-wide brownout an avoidable
    // consequence of over-equipping rather than a guaranteed side effect of every trip.
    public const float ThrusterActiveDraw = 2f;

    [Export]
    public bool HasPowerGrid { get; set; }

    /// <summary>Whether this ship starts with real gaps cut into its hull at
    /// <see cref="InitialBreaches"/> — true only for the Derelict.</summary>
    [Export]
    public bool HasHullBreaches { get; set; }

    /// <summary>Adds a minimal always-on power source feeding one pre-damaged conduit — the
    /// conduit fire hazard. Independent of <see cref="HasPowerGrid"/> so a ship can have one, the
    /// other, both, or neither.</summary>
    [Export]
    public bool HasFireHazard { get; set; }

    /// <summary>Whether this ship's atmosphere regenerates toward breathable over time when
    /// sealed (always-on scrubbers/O2 generation) — true only for a ship meant to be a safe
    /// base to retreat to, e.g. the Home Ship. Off by default: a Derelict's air, once spent,
    /// should stay spent even after its hull is patched.</summary>
    [Export]
    public bool HasLifeSupport { get; set; }

    /// <summary>Whether this ship has a Bunk (see BunkVerbTarget) with its own Deck-tracked wear —
    /// true only for the Home Ship. Independent of <see cref="HasPowerGrid"/>: a bunk doesn't
    /// need power, so this must not be nested inside that gate.</summary>
    [Export]
    public bool HasBunk { get; set; }

    public Deck Deck => _systems.Deck;

    public AtmosphereSystem? Atmosphere => _systems?.Atmosphere;

    /// <summary>This ship's whole simulation, owned as a plain object rather than as fields on
    /// this node — see <see cref="ShipSystems"/>. The node knows about scenes, frames and
    /// exports; the systems below it don't, which is what lets a ship keep simulating while
    /// nobody's aboard.</summary>
    private ShipSystems _systems = null!;

    /// <summary>False while the player is somewhere else and this ship isn't physically present
    /// (see TravelConsoleVerbTarget.SetShipPresence). It keeps simulating either way, just in
    /// coarse lumps rather than every frame. Defaults true so a ship nobody ever tells otherwise
    /// behaves exactly as before.</summary>
    public bool IsPresent { get; set; } = true;

    private PowerSystem? _power;
    private BatteryFixture? _battery;

    public override void _Ready()
    {
        if (DeckIndex > 0)
        {
            // Not the primary deck of my site — my own LayoutId/ProcedurallyGenerate are ignored;
            // my shape comes from whatever the primary deck's layout resolved as its SecondDeck.
            // Null (single-deck primary, or an unwired PrimaryDeckRef) leaves GridWidth/etc. at
            // this node's scene-authored defaults, which Derelict.tscn sets to an empty grid —
            // inert and harmless, exactly the "not every derelict has a second deck" case.
            ApplyLayout(PrimaryDeckRef?.SecondDeckLayout);
            LadderCell = PrimaryDeckRef?.LadderCell;
        }
        else if (ProcedurallyGenerate)
        {
            if (!string.IsNullOrEmpty(LayoutId))
            {
                GD.PushWarning($"[ShipSim] Both ProcedurallyGenerate and LayoutId ('{LayoutId}') are set on the same ship — ProcedurallyGenerate wins.");
            }

            var savePath = SavePathOverride ?? SaveManager.DefaultSavePath;
            var savedSeeds = SaveManager.TryReadShipLayoutSeeds(savePath);
            LayoutSeed = savedSeeds is not null && savedSeeds.TryGetValue(SaveId, out var savedSeed)
                ? savedSeed
                : new Random().Next();

            ApplyGeneratedLayout(ShipLayoutGenerator.Generate(LayoutSeed.Value));
        }
        else if (!string.IsNullOrEmpty(LayoutId))
        {
            ApplyLayout(ShipLayoutCatalog.TryGet(LayoutId));
        }

        var deck = new Deck();
        for (var i = 0; i < GridWidth; i++)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                deck.AddCell(new CellCoord(i, j));
            }
        }

        for (var i = 1; i <= WestCorridorLength; i++)
        {
            foreach (var j in DoorwayRows)
            {
                deck.AddCell(new CellCoord(-i, j));
            }
        }

        for (var i = 0; i < EastCorridorLength; i++)
        {
            foreach (var j in DoorwayRows)
            {
                deck.AddCell(new CellCoord(GridWidth + i, j));
            }
        }

        foreach (var splitColumn in RoomSplitColumns)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                if (!DoorwayRows.Contains(j))
                {
                    deck.SealEdge(new CellCoord(splitColumn - 1, j), new CellCoord(splitColumn, j));
                }
            }
        }

        // Atmosphere and wear exist on every ship, regardless of its other hazard flags — even a
        // never-breached ship (e.g. the Home Ship) needs a real AtmosphereSystem to bridge
        // against once an AirlockDoorVerbTarget links two ships' atmospheres.
        _systems = new ShipSystems(deck, hasLifeSupport: HasLifeSupport);

        if (HasHullBreaches)
        {
            foreach (var (cell, outside) in InitialBreaches)
            {
                Deck.BreachWallEdge(cell, outside);
            }

            SeedStructuralWear();
        }

        // Deferring to the next frame guarantees every breach for this ship is already
        // registered before seeding vacuum. Only the room(s) actually connected to a breach
        // start at vacuum: a room a closed door has kept sealed off from any breach starts
        // breathable, while a long-drifted derelict with breaches in both rooms starts airless
        // in both.
        CallDeferred(nameof(SeedVacuumFromInitialBreaches));

        // Unconditional (unlike the power-grid fixtures below): a physical door wears down
        // regardless of whether its ship has any electricity — a Derelict never has HasPowerGrid,
        // so without this its interior door would have no Condition/upkeep concept at all.
        Deck.AddFixture(new MachineFixture(InteriorDoorFixtureId, InteriorDoorCell, FixtureSurface.FloorUnderside) { PowerDraw = IdleDraw });

        // Also unconditional — a Station's destination-side airlock door needs a wear-tracked
        // fixture on ITS OWN ship regardless of whether that ship has a power grid (Stations
        // don't today).
        Deck.AddFixture(new MachineFixture(StationDestinationAirlockFixtureId, StationDestinationAirlockCell, FixtureSurface.FloorUnderside) { PowerDraw = IdleDraw });

        if (HasPowerGrid)
        {
            // None of the below are pre-connected — the player must run their own conduits from
            // the battery to each via ShipBuildTarget's free-form placement. Battery/Switch/
            // RechargeStation are player-install/uninstall-able construction parts instead (see
            // ShipBuildTarget's MachineType), seeded for the Home Ship through the same
            // Install*/Remove* calls a player action or save replay uses.
            Deck.AddFixture(new MachineFixture(TravelConsoleFixtureId, TravelConsoleCell, FixtureSurface.WallInner) { PowerDraw = IdleDraw });
            Deck.AddFixture(new MachineFixture(StationAirlockFixtureId, StationAirlockCell, FixtureSurface.FloorUnderside) { PowerDraw = IdleDraw });
            Deck.AddFixture(new MachineFixture(DerelictAirlockFixtureId, DerelictAirlockCell, FixtureSurface.FloorUnderside) { PowerDraw = IdleDraw });

            _power = _systems.EnsurePower();
        }

        if (HasBunk)
        {
            Deck.AddFixture(new MachineFixture(BunkFixtureId, BunkCell, FixtureSurface.FloorUnderside) { PowerDraw = IdleDraw });
        }

        if (HasFireHazard)
        {
            Deck.AddFixture(new MachineFixture(FireGeneratorFixtureId, FireGeneratorCell, FixtureSurface.WallInner));
            Deck.AddFixture(new ConduitFixture(DamagedConduitFixtureId, DamagedConduitCell, FixtureSurface.FloorUnderside)
            {
                Condition = 0.1f,
            });

            _power = _systems.EnsurePower();
            _power.MarkSource(new PowerNodeId(FireGeneratorFixtureId));

            _systems.EnableFire();
        }
    }

    /// <summary>Overwrites this ship's grid shape and hazard placement from a loaded
    /// <see cref="ShipLayoutCatalog"/> entry — public so NodeTests can call it directly with a
    /// hand-built definition, without needing to seed or file-load the static catalog. A null
    /// layout is a no-op. Must run before the Deck-building loop in <see cref="_Ready"/>.</summary>
    public void ApplyLayout(ShipLayoutCatalog.ShipLayoutDefinition? layout)
    {
        if (layout is null)
        {
            return;
        }

        GridWidth = layout.GridWidth;
        RoomSplitColumns = layout.RoomSplitColumns;
        EastCorridorLength = layout.EastCorridorLength;
        HasHullBreaches = layout.HasHullBreaches;
        HasFireHazard = layout.HasFireHazard;
        InitialBreaches = layout.InitialBreaches
            .Select(b => (new CellCoord(b.CellX, b.CellY), new CellCoord(b.OutsideX, b.OutsideY)))
            .ToArray();

        if (layout.FireGeneratorCell is { } fireGeneratorCell)
        {
            FireGeneratorCell = new CellCoord(fireGeneratorCell.X, fireGeneratorCell.Y);
        }

        if (layout.DamagedConduitCell is { } damagedConduitCell)
        {
            DamagedConduitCell = new CellCoord(damagedConduitCell.X, damagedConduitCell.Y);
        }

        if (layout.LadderCell is { } ladderCell)
        {
            LadderCell = new CellCoord(ladderCell.X, ladderCell.Y);
        }

        SecondDeckLayout = layout.SecondDeck;
    }

    /// <summary>Reuses <see cref="ApplyLayout"/> for the grid-shape/hazard half, and additionally
    /// records this ship's own loot list.</summary>
    public void ApplyGeneratedLayout(GeneratedShipLayout generated)
    {
        ApplyLayout(generated.Layout);
        LootSpawns = generated.Loot;
    }

    // Reuses AtmosphereSystem's own CellsConnectedToOutside instead of hand-rolling a second
    // flood-fill here — see CLAUDE.md's "one solver" rule.
    private void SeedVacuumFromInitialBreaches()
    {
        if (_systems is null)
        {
            return;
        }

        foreach (var cell in _systems.Atmosphere.CellsConnectedToOutside())
        {
            _systems.Atmosphere.ApplyExternalVolume(cell, AtmosphereVolume.Vacuum);
        }
    }

    // Placeholder/tunable — a found derelict should read as genuinely rough, not pristine: this
    // spans both the Maintain (>50%) and Repair (<=50%) upkeep tiers (see MaintenanceTier)
    // rather than sitting comfortably in one, so different tiles on the same wreck can need
    // different levels of work.
    private const float MinStartingStructuralHealth = 0.3f;
    private const float MaxStartingStructuralHealth = 0.9f;

    /// <summary>Gives every floor/ceiling/wall a random starting health instead of the default
    /// pristine 1.0 — only called for a ship worth marking HasHullBreaches, which should read as
    /// genuinely found-this-way. Uses the no-clamp, no-breach-side-effect absolute setters rather
    /// than DamageFloor/etc., which would risk an unwanted breach if a low roll landed near 0.
    /// Seeding every cell's edges from all 4 directions is simpler than tracking which edges will
    /// actually get a real wall spawned later — an edge that ends up unsealed/breached just never
    /// has its seeded value read.</summary>
    private void SeedStructuralWear()
    {
        var rng = new RandomNumberGenerator();
        foreach (var cell in Deck.Cells)
        {
            Deck.SetFloorHealth(cell, rng.RandfRange(MinStartingStructuralHealth, MaxStartingStructuralHealth));
            Deck.SetCeilingHealth(cell, rng.RandfRange(MinStartingStructuralHealth, MaxStartingStructuralHealth));

            foreach (var neighbor in new[]
            {
                new CellCoord(cell.X + 1, cell.Y),
                new CellCoord(cell.X - 1, cell.Y),
                new CellCoord(cell.X, cell.Y + 1),
                new CellCoord(cell.X, cell.Y - 1),
            })
            {
                Deck.SetWallHealth(cell, neighbor, rng.RandfRange(MinStartingStructuralHealth, MaxStartingStructuralHealth));
            }
        }
    }

    /// <summary>Full fidelity while the player is here; a coarse level-of-detail tick otherwise
    /// (see <see cref="ShipSystems.TickCoarse"/>). An absent ship still pays every cost in full —
    /// it banks elapsed time and spends it in one-second lumps rather than sixty per second — so
    /// a derelict left venting is still vented on return.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (IsPresent)
        {
            _systems.Tick(delta);
        }
        else
        {
            _systems.TickCoarse(delta);
        }
    }

    /// <summary>Breathable if this ship's atmosphere isn't wired up at all (e.g. queried before
    /// _Ready), otherwise the modeled cell's real volume — or, if <paramref name="cell"/> isn't
    /// one of this ship's own modeled Deck cells, whichever orthogonal neighbor IS modeled
    /// (falling back to Vacuum only if none are). That unmodeled case is reachable: a world-
    /// position-derived tile can land in the unmodeled seam between two docked ships' airlock
    /// corridors, including right at a *closed* door's boundary edge, where a floor-based
    /// conversion can round one tile too far even though the player never left this ship. Reading
    /// the nearest modeled neighbor gets that case right while still avoiding
    /// AtmosphereSystem.VolumeAt's hard KeyNotFoundException on a truly unrecognized cell.</summary>
    public AtmosphereVolume VolumeAt(CellCoord cell)
    {
        if (_systems is null)
        {
            return AtmosphereVolume.Breathable;
        }

        if (Deck.Cells.Contains(cell))
        {
            return _systems.Atmosphere.VolumeAt(cell);
        }

        foreach (var neighbor in cell.OrthogonalNeighbors())
        {
            if (Deck.Cells.Contains(neighbor))
            {
                return _systems.Atmosphere.VolumeAt(neighbor);
            }
        }

        return AtmosphereVolume.Vacuum;
    }

    /// <summary>Topologically connected to a source, (if this ship has a battery) that battery
    /// actually has charge, AND the circuit isn't currently overloaded (see IsOverloaded): a
    /// ship-wide brownout, not a per-device cutoff, so every fixture's IsPowered flips false at
    /// once when total demand exceeds BatteryCapacity, and recovers the instant it drops back
    /// under.</summary>
    public bool IsPowered(string fixtureId) =>
        _power is not null &&
        _power.IsPowered(new PowerNodeId(fixtureId)) &&
        (_battery is null || _battery.Condition > 0f) &&
        !IsOverloaded;

    /// <summary>Sum of PowerDraw over every fixture topologically reachable from a source —
    /// deliberately reads _power's raw powered set rather than the full IsPowered above, to avoid
    /// the circularity of "demand decides overload decides IsPowered decides demand." What the
    /// circuit is currently asking for, regardless of whether supply can cover it.
    ///
    /// Resolves the powered set once and tests membership, rather than asking _power.IsPowered
    /// per fixture — that made this one O(F) pass instead of F, and IsOverloaded calls it, so
    /// every per-frame IsPowered call paid for it too.</summary>
    public float DemandedPower()
    {
        if (_power is null)
        {
            return 0f;
        }

        var powered = _power.PoweredNodes();
        return Deck.Fixtures.Where(f => powered.Contains(new PowerNodeId(f.Id))).Sum(f => f.PowerDraw);
    }

    private bool IsOverloaded => DemandedPower() > BatteryCapacity;

    /// <summary>0-1 charge fraction for HUD/verb-suffix display — 0 for a ship with no battery.</summary>
    public float BatteryChargeFraction => _battery?.Condition ?? 0f;

    public void DrainBattery(float amount)
    {
        if (_battery is null)
        {
            return;
        }

        _battery.Condition = Mathf.Clamp(_battery.Condition - amount, 0f, 1f);
    }

    public void RechargeBattery(float amount)
    {
        if (_battery is null)
        {
            return;
        }

        _battery.Condition = Mathf.Clamp(_battery.Condition + amount, 0f, 1f);
    }

    /// <summary>Jumps straight to a saved charge value — the save/load counterpart to
    /// Drain/RechargeBattery's incremental changes.</summary>
    public void SetBatteryCharge(float value)
    {
        if (_battery is null)
        {
            return;
        }

        _battery.Condition = Mathf.Clamp(value, 0f, 1f);
    }

    public void SetSwitchOpen(bool isOpen)
    {
        if (Deck.Fixtures.FirstOrDefault(f => f.Id == SwitchFixtureId) is SwitchFixture switchFixture)
        {
            switchFixture.IsOpen = isOpen;
        }
    }

    // Battery/Switch/RechargeStation are player-install/uninstall-able construction parts (see
    // ShipBuildTarget's MachineType) rather than seeded in _Ready() — these are the single place
    // their fixtures get added to/removed from the Deck, called equally by a player action, the
    // Home Ship's default-layout seed, and a save replay.

    public void InstallBattery(CellCoord cell, FixtureSurface surface)
    {
        _battery = new BatteryFixture(BatteryFixtureId, cell, surface) { Condition = 1f };
        Deck.AddFixture(_battery);
        _power?.MarkSource(new PowerNodeId(BatteryFixtureId));
    }

    public void RemoveBattery()
    {
        Deck.RemoveFixture(BatteryFixtureId);
        _battery = null;
    }

    public void InstallSwitch(CellCoord cell, FixtureSurface surface) =>
        Deck.AddFixture(new SwitchFixture(SwitchFixtureId, cell, surface));

    public void RemoveSwitch() => Deck.RemoveFixture(SwitchFixtureId);

    public void InstallRechargeStation(CellCoord cell, FixtureSurface surface) =>
        Deck.AddFixture(new MachineFixture(RechargeFixtureId, cell, surface) { PowerDraw = IdleDraw });

    public void RemoveRechargeStation() => Deck.RemoveFixture(RechargeFixtureId);

    // Thrusters take a caller-supplied id rather than a single fixed constant — there can be many
    // installed at once, one per edge ShipBuildTarget tracks. Starts at Condition = 0f (empty),
    // not full — a freshly installed thruster has no fuel until a real N2 tank is docked;
    // ShipBuildTarget's own save/load callers override this via an explicit savedState when a
    // real charge should carry over.
    public void InstallThruster(string id, CellCoord cell, FixtureSurface surface) =>
        Deck.AddFixture(new ThrusterFixture(id, cell, surface) { Condition = 0f, PowerDraw = IdleDraw });

    public void RemoveThruster(string id) => Deck.RemoveFixture(id);

    /// <summary>PowerDraw stays at the base default (0) — a shelf/bin draws no power, unlike
    /// every other installable fixture here.</summary>
    public void InstallStorage(string id, CellCoord cell, FixtureSurface surface) =>
        Deck.AddFixture(new StorageFixture(id, cell, surface));

    public void RemoveStorage(string id) => Deck.RemoveFixture(id);

    public void RechargeThruster(string id, float amount)
    {
        if (Deck.Fixtures.FirstOrDefault(f => f.Id == id) is ThrusterFixture thruster)
        {
            thruster.Condition = Mathf.Clamp(thruster.Condition + amount, 0f, 1f);
        }
    }

    public void SetThrusterCharge(string id, float value)
    {
        if (Deck.Fixtures.FirstOrDefault(f => f.Id == id) is ThrusterFixture thruster)
        {
            thruster.Condition = Mathf.Clamp(value, 0f, 1f);
        }
    }

    /// <summary>This ship's live sim state for the save file — see <see cref="IShipStateSaveable"/>.
    /// Captures what the air and fire are currently doing, which no other save contract covers:
    /// ShipBuildTarget records what has been built, not what the atmosphere has since done to it.</summary>
    public ShipStateSaveData CaptureShipState()
    {
        var state = new ShipStateSaveData();

        foreach (var (cell, volume) in _systems.CaptureVolumes())
        {
            state.Volumes.Add(new CellVolume(cell.X, cell.Y, volume.Pressure, volume.O2Fraction, volume.Temperature));
        }

        foreach (var cell in Deck.Fires)
        {
            state.Fires.Add(new TileCoord(cell.X, cell.Y));
        }

        return state;
    }

    public void ApplyShipState(ShipStateSaveData state)
    {
        _systems.ApplyVolumes(state.Volumes.Select(v =>
            (new CellCoord(v.X, v.Y), new AtmosphereVolume(v.Pressure, v.O2Fraction, v.Temperature))));

        _systems.ApplyFires(state.Fires.Select(f => new CellCoord(f.X, f.Y)));
    }

    /// <summary>0-1 charge fraction for a specific thruster's own N2 tank — 0 if the given id
    /// isn't (or is no longer) an installed thruster. Draining during travel reads/writes
    /// ThrusterFixture.Condition directly off Deck.Fixtures instead, since that loop already
    /// holds a live fixture reference.</summary>
    public float ThrusterChargeFraction(string id) =>
        Deck.Fixtures.FirstOrDefault(f => f.Id == id) is ThrusterFixture thruster ? thruster.Condition : 0f;
}
