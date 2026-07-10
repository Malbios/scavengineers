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
/// full 1m-tile grid matching the two-room floor plan already built in the scene (Room 1 =
/// tiles i 0-5, Room 2 = i 6-11, both j 0-5), optionally hosting a hull breach (atmosphere)
/// and/or a generator/switch/recharge-station power grid, toggled per scene via the exported
/// flags. Real, data-driven ship layouts (arbitrary footprints) are still separate, larger
/// future work — this is a fixed-shape stand-in, just no longer a single abstract cell.
/// </summary>
public partial class ShipSim : Node
{
    private const int GridWidth = 12;
    private const int GridDepth = 6;

    // Room 1 = i 0-5, Room 2 = i 6-11 (matches MidWallA/B at local x=3). The doorway gap
    // (MidWallA/B only cover j 0,1 and 4,5) is j 2,3 — the only edges between the rooms that
    // aren't a permanent wall.
    private const int RoomSplitIndex = 6;
    private static readonly int[] DoorwayRows = [2, 3];

    private static readonly CellCoord GeneratorCell = new(5, 0);
    private static readonly CellCoord SwitchCell = new(5, 0);
    private static readonly CellCoord RechargeCell = new(9, 0);

    // Bridges the real 4-tile gap between the switch and the recharge station now that
    // fixtures sit at their actual scene positions instead of assumed-adjacent placeholders
    // (docs/project-plan.md Appendix A7 — conduits physically route power between fixtures).
    private static readonly CellCoord[] ConduitCells = [new(6, 0), new(7, 0), new(8, 0)];

    // A device the player must wire up themselves via ShipBuildTarget's free-form conduit
    // placement — deliberately not pre-connected. Tile (7,4) sits 3 tiles south of the existing
    // conduit_1 at (7,0); the tiles between are left empty for the player to wire freely.
    private static readonly CellCoord ComputerCell = new(7, 4);

    // The Derelict's two starting hull breaches — a real gap cut into the wall mesh at these
    // tiles, repaired via ShipBuildTarget's generic wall-building (docs/project-plan.md
    // Appendix A7/A8) rather than a dedicated repair object.
    private static readonly CellCoord[] InitialBreachCells = [new(6, 5), new(3, 0)];

    // Room 1, clear of the Derelict's wall-panel pickup (1,1) and breach (3,0) — a minimal,
    // switch-less power source just to energize one already-damaged conduit (docs/project-plan.md
    // Appendix A7's fire loop), kept deliberately separate from HasPowerGrid's home-ship-shaped
    // generator/switch/recharge chain. Room 1 rather than Room 2 so each room carries one
    // distinct hazard instead of stacking the conduit alongside Room 2's own breach.
    private static readonly CellCoord FireGeneratorCell = new(1, 4);
    private static readonly CellCoord DamagedConduitCell = new(1, 3);

    public const string GeneratorFixtureId = "generator";
    public const string SwitchFixtureId = "switch";
    public const string RechargeFixtureId = "recharge_station";
    public const string FireGeneratorFixtureId = "fire_generator";
    public const string DamagedConduitFixtureId = "damaged_conduit";
    public const string ComputerFixtureId = "computer";

    [Export]
    public bool HasPowerGrid { get; set; }

    /// <summary>Whether this ship starts with real gaps cut into its hull at
    /// <see cref="InitialBreachCells"/> — true only for the Derelict.</summary>
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

    public override void _Ready()
    {
        Deck = new Deck();
        for (var i = 0; i < GridWidth; i++)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                Deck.AddCell(new CellCoord(i, j));
            }
        }

        for (var j = 0; j < GridDepth; j++)
        {
            if (!DoorwayRows.Contains(j))
            {
                Deck.SealEdge(new CellCoord(RoomSplitIndex - 1, j), new CellCoord(RoomSplitIndex, j));
            }
        }

        // Always present, even for a never-breached ship (e.g. the Home Ship) — it needs a
        // real AtmosphereSystem to bridge against once an AirlockDoorVerbTarget links the two
        // ships' atmospheres. A never-breached deck just sits at Breathable and never changes.
        _atmosphere = new AtmosphereSystem(Deck, hasLifeSupport: HasLifeSupport);

        if (HasHullBreaches)
        {
            foreach (var cell in InitialBreachCells)
            {
                Deck.BreachHull(cell);
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
            Deck.AddFixture(new MachineFixture(GeneratorFixtureId, GeneratorCell, FixtureSurface.WallInner));
            Deck.AddFixture(new SwitchFixture(SwitchFixtureId, SwitchCell, FixtureSurface.WallInner));

            for (var i = 0; i < ConduitCells.Length; i++)
            {
                Deck.AddFixture(new ConduitFixture($"conduit_{i}", ConduitCells[i], FixtureSurface.FloorUnderside));
            }

            Deck.AddFixture(new MachineFixture(RechargeFixtureId, RechargeCell, FixtureSurface.WallInner));
            Deck.AddFixture(new MachineFixture(ComputerFixtureId, ComputerCell, FixtureSurface.WallInner));

            _power = new PowerSystem(Deck);
            _power.MarkSource(new PowerNodeId(GeneratorFixtureId));
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

    private void SeedVacuumFromInitialBreaches()
    {
        if (_atmosphere is null)
        {
            return;
        }

        var visited = new HashSet<CellCoord>();
        var queue = new Queue<CellCoord>();
        foreach (var breach in Deck.HullBreaches)
        {
            if (visited.Add(breach))
            {
                queue.Enqueue(breach);
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            _atmosphere.ApplyExternalVolume(cell, AtmosphereVolume.Vacuum);

            foreach (var neighbor in AdjacentCells(cell))
            {
                if (Deck.Cells.Contains(neighbor) && !Deck.IsEdgeSealed(cell, neighbor) && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    private static IEnumerable<CellCoord> AdjacentCells(CellCoord cell)
    {
        yield return cell with { X = cell.X + 1 };
        yield return cell with { X = cell.X - 1 };
        yield return cell with { Y = cell.Y + 1 };
        yield return cell with { Y = cell.Y - 1 };
    }

    public override void _PhysicsProcess(double delta)
    {
        _atmosphere?.Tick(delta);
        _fire?.Tick(delta);
    }

    public AtmosphereVolume VolumeAt(CellCoord cell) =>
        _atmosphere?.VolumeAt(cell) ?? AtmosphereVolume.Breathable;

    public bool IsPowered(string fixtureId) =>
        _power is not null && _power.IsPowered(new PowerNodeId(fixtureId));

    public void SetSwitchOpen(bool isOpen)
    {
        if (Deck.Fixtures.FirstOrDefault(f => f.Id == SwitchFixtureId) is SwitchFixture switchFixture)
        {
            switchFixture.IsOpen = isOpen;
        }
    }
}
