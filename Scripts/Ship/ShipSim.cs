using System;
using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Hazards;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Owns and ticks a real Scavengineers.Sim <see cref="ShipModel.Deck"/> for a greybox ship: a
/// full 1m-tile grid matching the room floor plan already built in the scene (rooms are
/// <see cref="GridWidth"/> tiles wide, split into columns by <see cref="RoomSplitColumns"/>,
/// both <see cref="GridDepth"/> deep), optionally hosting a hull breach (atmosphere) and/or a
/// battery/switch/recharge-station power grid, toggled per scene via the exported flags. Real,
/// data-driven ship layouts (arbitrary footprints) are still separate, larger future work —
/// this is a fixed-shape stand-in, just no longer a single abstract cell.
/// </summary>
public partial class ShipSim : Node
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
    /// boundary — real Deck.Cells, not a separate grid, so the same floor/wall/ceiling panel and
    /// wall-removal systems apply to them automatically. 0 by default (opt-in); only the Home
    /// Ship's airlock corridors use these today.</summary>
    [Export]
    public int WestCorridorLength { get; set; }

    [Export]
    public int EastCorridorLength { get; set; }

    private static readonly int[] DoorwayRows = [2, 3];

    // Every device below (the 4 further down) is added unwired — the player must run their own
    // conduits via ShipBuildTarget's free-form placement to connect any of them to the battery.
    // Battery/Switch/RechargeStation cells used to live here too, but they're now player-
    // install/uninstall-able construction parts (see ShipBuildTarget's MachineType) — their
    // cells moved there, since only it needs them now (for the default-seed replay). These stay
    // static/shared, not per-layout data: they only ever fire under HasPowerGrid, which today
    // only the Home Ship sets.
    private static readonly CellCoord TravelConsoleCell = new(0, 0);
    private static readonly CellCoord InteriorDoorCell = new(5, 2);
    private static readonly CellCoord StationAirlockCell = new(0, 2);
    private static readonly CellCoord DerelictAirlockCell = new(11, 2);

    /// <summary>The Derelict's starting hull breaches — a real gap cut into the wall mesh at
    /// these edges, repaired via ShipBuildTarget's generic wall-building (docs/project-plan.md
    /// Appendix A7/A8) rather than a dedicated repair object. Paired with each edge's "outside"
    /// neighbor since a wall breach is per-edge (Deck.BreachWallEdge), not per-cell. Instance
    /// property (not the static default this used to be) so <see cref="ApplyLayout"/> can give
    /// different derelicts different breach positions — the field default below is today's
    /// exact original value, so a ship with no <see cref="LayoutId"/> behaves identically to
    /// before this became data-driven.</summary>
    public (CellCoord Cell, CellCoord Outside)[] InitialBreaches { get; set; } =
        [(new(6, 5), new(6, 6)), (new(3, 0), new(3, -1))];

    /// <summary>A minimal, switch-less power source just to energize one already-damaged
    /// conduit (docs/project-plan.md Appendix A7's fire loop), kept deliberately separate from
    /// HasPowerGrid's home-ship-shaped generator/switch/recharge chain. Instance property (see
    /// <see cref="InitialBreaches"/>'s own doc comment for why); default matches the original
    /// Room 1 placement.</summary>
    public CellCoord FireGeneratorCell { get; set; } = new(1, 4);

    public CellCoord DamagedConduitCell { get; set; } = new(1, 3);

    /// <summary>Opt-in id into <see cref="ShipLayoutCatalog"/> — empty (the default) means this
    /// ship keeps whatever its own exported fields/defaults already say, exactly as before this
    /// existed. Only Derelict-style ships are expected to ever set this.</summary>
    [Export]
    public string LayoutId { get; set; } = "";

    /// <summary>Rolls a random <see cref="ShipLayoutGenerator"/> layout instead of reading
    /// <see cref="LayoutId"/> from the catalog — wins over <see cref="LayoutId"/> if both are
    /// somehow set. No save persistence yet (see docs on the seed itself, added in a later
    /// stage) — every boot currently rolls fresh.</summary>
    [Export]
    public bool ProcedurallyGenerate { get; set; }

    /// <summary>This ship's own procedurally-generated loot list, if any — empty unless
    /// <see cref="ApplyGeneratedLayout"/> was called. Read by ShipBuildTarget.SpawnGeneratedLoot;
    /// spawning itself lives there since it already owns the tile-to-world conversion and the
    /// generic dropped-item mesh/shape/material every ship scene already wires.</summary>
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

    // Placeholder/tunable — one power cell (see StationConsoleVerbTarget's economy) restores
    // this fraction of a full charge.
    public const float PowerCellRechargeAmount = 0.5f;

    [Export]
    public bool HasPowerGrid { get; set; }

    /// <summary>Whether this ship starts with real gaps cut into its hull at
    /// <see cref="InitialBreaches"/> — true only for the Derelict.</summary>
    [Export]
    public bool HasHullBreaches { get; set; }

    /// <summary>Adds a minimal always-on power source feeding one pre-damaged conduit — the
    /// conduit fire hazard (docs/project-plan.md Appendix A7). Independent of
    /// <see cref="HasPowerGrid"/> so a ship can have one, the other, both, or neither.</summary>
    [Export]
    public bool HasFireHazard { get; set; }

    /// <summary>Whether this ship's atmosphere regenerates toward breathable over time when
    /// sealed (always-on scrubbers/O2 generation) — true only for a ship meant to be a safe
    /// base to retreat to, e.g. the Home Ship. Off by default: a Derelict's air, once spent,
    /// should stay spent even after its hull is patched.</summary>
    [Export]
    public bool HasLifeSupport { get; set; }

    public Deck Deck { get; private set; } = null!;

    public AtmosphereSystem? Atmosphere => _atmosphere;

    private AtmosphereSystem? _atmosphere;
    private PowerSystem? _power;
    private FireSystem? _fire;
    private BatteryFixture? _battery;

    public override void _Ready()
    {
        if (ProcedurallyGenerate)
        {
            if (!string.IsNullOrEmpty(LayoutId))
            {
                GD.PushWarning($"[ShipSim] Both ProcedurallyGenerate and LayoutId ('{LayoutId}') are set on the same ship — ProcedurallyGenerate wins.");
            }

            ApplyGeneratedLayout(ShipLayoutGenerator.Generate(new Random().Next()));
        }
        else if (!string.IsNullOrEmpty(LayoutId))
        {
            ApplyLayout(ShipLayoutCatalog.TryGet(LayoutId));
        }

        Deck = new Deck();
        for (var i = 0; i < GridWidth; i++)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                Deck.AddCell(new CellCoord(i, j));
            }
        }

        for (var i = 1; i <= WestCorridorLength; i++)
        {
            foreach (var j in DoorwayRows)
            {
                Deck.AddCell(new CellCoord(-i, j));
            }
        }

        for (var i = 0; i < EastCorridorLength; i++)
        {
            foreach (var j in DoorwayRows)
            {
                Deck.AddCell(new CellCoord(GridWidth + i, j));
            }
        }

        foreach (var splitColumn in RoomSplitColumns)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                if (!DoorwayRows.Contains(j))
                {
                    Deck.SealEdge(new CellCoord(splitColumn - 1, j), new CellCoord(splitColumn, j));
                }
            }
        }

        // Always present, even for a never-breached ship (e.g. the Home Ship) — it needs a
        // real AtmosphereSystem to bridge against once an AirlockDoorVerbTarget links the two
        // ships' atmospheres. A never-breached deck just sits at Breathable and never changes.
        _atmosphere = new AtmosphereSystem(Deck, hasLifeSupport: HasLifeSupport);

        if (HasHullBreaches)
        {
            foreach (var (cell, outside) in InitialBreaches)
            {
                Deck.BreachWallEdge(cell, outside);
            }
        }

        // Deferring to the next frame guarantees every breach for this ship is already
        // registered before seeding vacuum. Only the room(s) actually connected to a breach
        // start at vacuum (not the whole ship uniformly): a room a closed door has kept sealed
        // off from any breach starts breathable, while a long-drifted derelict with real
        // breaches in both rooms starts airless in both, for the right reason (each room really
        // is holed) rather than a blanket assumption.
        CallDeferred(nameof(SeedVacuumFromInitialBreaches));

        if (HasPowerGrid)
        {
            // None of the below are pre-connected — the player must run their own conduits
            // from the battery to every one of them via ShipBuildTarget's free-form placement.
            // Battery/Switch/RechargeStation aren't seeded here at all anymore — they're
            // player-install/uninstall-able construction parts (see ShipBuildTarget's
            // MachineType), seeded (for the Home Ship) through the exact same
            // Install*/Remove* calls below that a player action or a save replay uses.
            Deck.AddFixture(new MachineFixture(TravelConsoleFixtureId, TravelConsoleCell, FixtureSurface.WallInner));
            Deck.AddFixture(new MachineFixture(InteriorDoorFixtureId, InteriorDoorCell, FixtureSurface.FloorUnderside));
            Deck.AddFixture(new MachineFixture(StationAirlockFixtureId, StationAirlockCell, FixtureSurface.FloorUnderside));
            Deck.AddFixture(new MachineFixture(DerelictAirlockFixtureId, DerelictAirlockCell, FixtureSurface.FloorUnderside));

            _power = new PowerSystem(Deck);
        }

        if (HasFireHazard)
        {
            Deck.AddFixture(new MachineFixture(FireGeneratorFixtureId, FireGeneratorCell, FixtureSurface.WallInner));
            Deck.AddFixture(new ConduitFixture(DamagedConduitFixtureId, DamagedConduitCell, FixtureSurface.FloorUnderside)
            {
                Condition = 0.1f,
            });

            _power ??= new PowerSystem(Deck);
            _power.MarkSource(new PowerNodeId(FireGeneratorFixtureId));

            _fire = new FireSystem(Deck, _atmosphere, _power);
        }
    }

    /// <summary>Overwrites this ship's grid shape and hazard placement from a loaded
    /// <see cref="ShipLayoutCatalog"/> entry — public (not private) so NodeTests can call it
    /// directly with a hand-built <see cref="ShipLayoutCatalog.ShipLayoutDefinition"/>, without
    /// needing to seed or file-load the static catalog at all. A null layout (unset/unknown
    /// LayoutId) is a no-op. Must run before the Deck-building loop in <see cref="_Ready"/>.</summary>
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
    }

    /// <summary>Applies a procedurally-generated layout (see <see cref="ShipLayoutGenerator"/>) —
    /// reuses <see cref="ApplyLayout"/> for the grid-shape/hazard half, and additionally records
    /// this ship's own loot list. Must run at the same point <see cref="ApplyLayout"/> does, for
    /// the same reason.</summary>
    public void ApplyGeneratedLayout(GeneratedShipLayout generated)
    {
        ApplyLayout(generated.Layout);
        LootSpawns = generated.Loot;
    }

    // Reuses AtmosphereSystem's own CellsConnectedToOutside (one ConnectivitySolver.FindComponents
    // pass over the same graph Tick() already partitions every frame) instead of hand-rolling a
    // second flood-fill here — see CLAUDE.md's "one solver" rule.
    private void SeedVacuumFromInitialBreaches()
    {
        if (_atmosphere is null)
        {
            return;
        }

        foreach (var cell in _atmosphere.CellsConnectedToOutside())
        {
            _atmosphere.ApplyExternalVolume(cell, AtmosphereVolume.Vacuum);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _atmosphere?.Tick(delta);
        _fire?.Tick(delta);
    }

    /// <summary>Breathable if this ship's atmosphere isn't wired up at all (e.g. queried before
    /// _Ready), otherwise the modeled cell's real volume — or, if <paramref name="cell"/> isn't
    /// one of this ship's own modeled Deck cells at all, whichever orthogonal neighbor IS modeled
    /// (falling back to Vacuum only if none are). That unmodeled case is reachable in practice: a
    /// world-position-derived tile (see ShipAtmosphereZone.TileAt, used for the player's own
    /// current-cell O2 read) can land in the unmodeled seam between two docked ships' airlock
    /// corridors, just past this ship's own WestCorridorLength/EastCorridorLength — including
    /// right at a *closed* door's own boundary edge, where TileAt's floor-based conversion can
    /// round one tile too far even though the player never left this ship. Reading the nearest
    /// modeled neighbor instead of blanket Vacuum gets that case right (this ship's own real air)
    /// while still avoiding AtmosphereSystem.VolumeAt's own hard KeyNotFoundException on a truly
    /// unrecognized cell, and still reads Vacuum when the neighbor is genuinely vented too (the
    /// original crash-prevention scenario this fallback exists for).</summary>
    public AtmosphereVolume VolumeAt(CellCoord cell)
    {
        if (_atmosphere is null)
        {
            return AtmosphereVolume.Breathable;
        }

        if (Deck.Cells.Contains(cell))
        {
            return _atmosphere.VolumeAt(cell);
        }

        foreach (var neighbor in cell.OrthogonalNeighbors())
        {
            if (Deck.Cells.Contains(neighbor))
            {
                return _atmosphere.VolumeAt(neighbor);
            }
        }

        return AtmosphereVolume.Vacuum;
    }

    /// <summary>Topologically connected to a source AND (if this ship has a battery at all)
    /// that battery actually has charge — a ship with no battery (e.g. the Derelict, whose only
    /// source is the always-on fire hazard generator) is never gated by this second check.</summary>
    public bool IsPowered(string fixtureId) =>
        _power is not null &&
        _power.IsPowered(new PowerNodeId(fixtureId)) &&
        (_battery is null || _battery.Condition > 0f);

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

    // Battery/Switch/RechargeStation are player-install/uninstall-able construction parts
    // (see ShipBuildTarget's MachineType) rather than seeded in _Ready() — these are the
    // single place their fixtures get added to/removed from the Deck, called equally by a
    // fresh player action, the Home Ship's own default-layout seed, and a save replay.

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
        Deck.AddFixture(new MachineFixture(RechargeFixtureId, cell, surface));

    public void RemoveRechargeStation() => Deck.RemoveFixture(RechargeFixtureId);
}
