using Godot;

namespace Scavengineers.Scripts.Ship;

/// <summary>A passive light that follows ShipSim.IsPowered(FixtureId) every tick — no verb of its
/// own.</summary>
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
