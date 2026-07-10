using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// Links two otherwise-independent <see cref="AtmosphereSystem"/>s (e.g. two docked ships) at
/// one cell each. Deliberately a bolt-on, not a merge of the two ships' connectivity graphs —
/// see docs/architecture/atmosphere-power-sim.md. Reuses the same lumped-scalar equalization
/// idea <see cref="AtmosphereSystem"/> already applies within a single deck, just gradually
/// (via a rate) instead of instantly, since the two sides are only linked through an airlock.
/// </summary>
public sealed class AirlockBridge(AtmosphereSystem systemA, CellCoord cellA, AtmosphereSystem systemB, CellCoord cellB)
{
    private const double EqualizeRatePerSecond = 0.5;

    public bool IsOpen { get; set; }

    public void Tick(double dt)
    {
        if (!IsOpen)
        {
            return;
        }

        var a = systemA.VolumeAt(cellA);
        var b = systemB.VolumeAt(cellB);

        var averagePressure = (a.Pressure + b.Pressure) / 2;
        var averageO2 = (a.O2Fraction + b.O2Fraction) / 2;
        var averageTemperature = (a.Temperature + b.Temperature) / 2;

        var factor = Math.Clamp(EqualizeRatePerSecond * dt, 0, 1);

        systemA.ApplyExternalVolume(cellA, a with
        {
            Pressure = Lerp(a.Pressure, averagePressure, factor),
            O2Fraction = Lerp(a.O2Fraction, averageO2, factor),
            Temperature = Lerp(a.Temperature, averageTemperature, factor),
        });

        systemB.ApplyExternalVolume(cellB, b with
        {
            Pressure = Lerp(b.Pressure, averagePressure, factor),
            O2Fraction = Lerp(b.O2Fraction, averageO2, factor),
            Temperature = Lerp(b.Temperature, averageTemperature, factor),
        });
    }

    private static double Lerp(double from, double to, double factor) => from + (to - from) * factor;
}
