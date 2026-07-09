using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Owns and ticks the real Scavengineers.Sim atmosphere for GreyboxRoom's demo deck: one
/// cell, pre-breached. This is a deliberately minimal stand-in ship for proving the verb
/// system is wired to the real sim (docs/architecture/ship-model.md /
/// docs/architecture/atmosphere-power-sim.md) — real, data-driven ship layouts are separate,
/// larger future work.
/// </summary>
public partial class ShipSim : Node
{
    public static readonly CellCoord DemoCell = new(0, 0);

    public Deck Deck { get; private set; } = null!;

    private AtmosphereSystem _atmosphere = null!;

    public override void _Ready()
    {
        Deck = new Deck();
        Deck.AddCell(DemoCell);
        Deck.BreachHull(DemoCell);
        _atmosphere = new AtmosphereSystem(Deck);
    }

    public override void _PhysicsProcess(double delta)
    {
        _atmosphere.Tick(delta);
    }

    public AtmosphereVolume VolumeAt(CellCoord cell) => _atmosphere.VolumeAt(cell);
}
