using System.Linq;
using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Owns and ticks a real Scavengineers.Sim <see cref="ShipModel.Deck"/> for a greybox ship:
/// one abstract cell, optionally hosting a hull breach (atmosphere) and/or a generator/
/// switch/recharge-station power grid, toggled per scene via the exported flags. This is a
/// deliberately minimal stand-in ship for proving the sim is wired to real gameplay
/// (docs/architecture/atmosphere-power-sim.md) — real, data-driven ship layouts are separate,
/// larger future work.
/// </summary>
public partial class ShipSim : Node
{
    public static readonly CellCoord DemoCell = new(0, 0);

    private static readonly CellCoord GeneratorCell = new(0, 0);
    private static readonly CellCoord SwitchCell = new(1, 0);
    private static readonly CellCoord RechargeCell = new(2, 0);

    public const string GeneratorFixtureId = "generator";
    public const string SwitchFixtureId = "switch";
    public const string RechargeFixtureId = "recharge_station";

    [Export]
    public bool HasHullBreach { get; set; }

    [Export]
    public bool HasPowerGrid { get; set; }

    public Deck Deck { get; private set; } = null!;

    private AtmosphereSystem? _atmosphere;
    private PowerSystem? _power;

    public override void _Ready()
    {
        Deck = new Deck();
        Deck.AddCell(DemoCell);

        if (HasHullBreach)
        {
            Deck.BreachHull(DemoCell);
            _atmosphere = new AtmosphereSystem(Deck);
        }

        if (HasPowerGrid)
        {
            Deck.AddFixture(new MachineFixture(GeneratorFixtureId, GeneratorCell, FixtureSurface.WallInner));
            Deck.AddFixture(new SwitchFixture(SwitchFixtureId, SwitchCell, FixtureSurface.WallInner));
            Deck.AddFixture(new MachineFixture(RechargeFixtureId, RechargeCell, FixtureSurface.WallInner));

            _power = new PowerSystem(Deck);
            _power.MarkSource(new PowerNodeId(GeneratorFixtureId));
        }
    }

    public override void _PhysicsProcess(double delta) => _atmosphere?.Tick(delta);

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
