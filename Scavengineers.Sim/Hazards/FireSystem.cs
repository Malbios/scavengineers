using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Hazards;

/// <summary>
/// The conduit fire loop from docs/project-plan.md Appendix A7: a conduit that's powered,
/// damaged, and sitting in enough oxygen sparks and burns — no scripting, just those three
/// existing states (power/condition/atmosphere) intersecting. A pure reader of
/// <see cref="ShipModel.Deck"/>'s fixtures plus the ship's own <see cref="Power.PowerSystem"/>
/// and <see cref="Atmosphere.AtmosphereSystem"/>; writes fire state back onto the Deck (mirrors
/// how hull breaches are tracked) and atmosphere state back via
/// <see cref="AtmosphereSystem.ApplyExternalVolume"/> (the same hook <see cref="AirlockBridge"/>
/// already uses).
/// </summary>
public sealed class FireSystem(Deck deck, AtmosphereSystem atmosphere, PowerSystem power)
{
    private const float DamagedConditionThreshold = 0.3f;
    private const double IgnitionO2Threshold = 0.1;
    private const double ExtinguishO2Threshold = 0.05;
    private const double O2ConsumptionPerSecond = 0.01;
    private const double HeatGainPerSecond = 5.0;

    public void Tick(double dt)
    {
        foreach (var fixture in deck.Fixtures)
        {
            if (fixture is not ConduitFixture conduit || deck.IsOnFire(conduit.Tile))
            {
                continue;
            }

            if (conduit.Condition >= DamagedConditionThreshold)
            {
                continue;
            }

            if (!power.IsPowered(new PowerNodeId(conduit.Id)))
            {
                continue;
            }

            if (atmosphere.VolumeAt(conduit.Tile).O2Fraction < IgnitionO2Threshold)
            {
                continue;
            }

            deck.IgniteFire(conduit.Tile);
        }

        foreach (var cell in deck.Fires.ToList())
        {
            var volume = atmosphere.VolumeAt(cell);

            if (volume.O2Fraction < ExtinguishO2Threshold)
            {
                deck.ExtinguishFire(cell);
                continue;
            }

            atmosphere.ApplyExternalVolume(cell, volume with
            {
                O2Fraction = Math.Max(0, volume.O2Fraction - O2ConsumptionPerSecond * dt),
                Temperature = volume.Temperature + HeatGainPerSecond * dt,
            });
        }
    }
}
