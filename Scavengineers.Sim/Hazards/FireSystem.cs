using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Hazards;

/// <summary>The conduit fire loop: a conduit that's powered, damaged, and sitting in enough
/// oxygen sparks and burns — no scripting, just those three existing states (power/condition/
/// atmosphere) intersecting. A pure reader of <see cref="ShipModel.Deck"/>'s fixtures plus the
/// ship's own <see cref="Power.PowerSystem"/> and <see cref="Atmosphere.AtmosphereSystem"/>;
/// writes fire state back onto the Deck and atmosphere state back via
/// <see cref="AtmosphereSystem.ApplyExternalVolume"/> (the same hook <see cref="AirlockBridge"/>
/// uses).</summary>
public sealed class FireSystem(Deck deck, AtmosphereSystem atmosphere, PowerSystem power)
{
    private const float DamagedConditionThreshold = 0.3f;
    private const double IgnitionO2Threshold = 0.1;
    private const double ExtinguishO2Threshold = 0.05;
    private const double O2ConsumptionPerSecond = 0.01;
    private const double HeatGainPerSecond = 5.0;

    // Placeholder/tunable — ~20s to fully degrade a neighbor from full Condition. Runs before the
    // ignition check below in the same Tick, so a conduit heat-damaged below
    // DamagedConditionThreshold this tick can ignite via that same check on a later tick — spread
    // reuses the existing ignition rule rather than adding a second one.
    private const float HeatDamagePerSecond = 0.05f;

    public void Tick(double dt)
    {
        foreach (var burningCell in deck.Fires)
        {
            foreach (var cell in burningCell.OrthogonalNeighbors().Append(burningCell))
            {
                if (cell != burningCell && (!deck.Cells.Contains(cell) || deck.IsEdgeSealed(burningCell, cell)))
                {
                    continue;
                }

                foreach (var conduit in deck.Fixtures.OfType<ConduitFixture>().Where(f => f.Tile == cell))
                {
                    conduit.Condition = Math.Max(0f, conduit.Condition - HeatDamagePerSecond * (float)dt);
                }
            }
        }

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
