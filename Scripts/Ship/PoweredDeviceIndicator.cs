using Godot;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The visible payoff for wiring a device up via ShipBuildTarget — no verb of its own,
/// just a passive light that follows ShipSim.IsPowered(FixtureId) every tick.
/// </summary>
public partial class PoweredDeviceIndicator : Node
{
    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public string FixtureId { get; set; } = "";

    [Export]
    public Light3D? IndicatorLight { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        if (IndicatorLight is not null)
        {
            IndicatorLight.Visible = ShipSimRef?.IsPowered(FixtureId) ?? false;
        }
    }
}
