using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>Links two otherwise-independent <see cref="AtmosphereSystem"/>s (e.g. two docked
/// ships) — deliberately a bolt-on, not a merge of the two ships' connectivity graphs.
///
/// When neither side has its own path to Outside, this just pools the two named cells toward
/// each other (an open airlock as one more diffusion-style edge). Once either side already has
/// its own leak to vacuum, the bridge stops averaging and instead marks both cells via
/// <see cref="AtmosphereSystem.MarkExternallyVented"/>: each side's own Tick then vents its
/// entire connected component — including the bridged cell — uniformly toward vacuum, the same
/// way a direct hull breach would. This matches real depressurization (the whole connected
/// volume drops together, no distance-based lag).</summary>
public sealed class AirlockBridge(AtmosphereSystem systemA, CellCoord cellA, AtmosphereSystem systemB, CellCoord cellB)
{
    // Placeholder/tunable — only used by the non-leaking averaging branch below; the leaking
    // branch just marks both cells and lets each system's own Vent handle the actual drain.
    private const double EqualizeRatePerSecond = 5.0;

    public bool IsOpen { get; set; }

    public void Tick(double dt)
    {
        if (!IsOpen)
        {
            return;
        }

        if (systemA.IsConnectedToOutside(cellA) || systemB.IsConnectedToOutside(cellB))
        {
            systemA.MarkExternallyVented(cellA);
            systemB.MarkExternallyVented(cellB);
            return;
        }

        var volumeA = systemA.VolumeAt(cellA);
        var volumeB = systemB.VolumeAt(cellB);
        var factor = Math.Clamp(EqualizeRatePerSecond * dt, 0, 1);

        var averagePressure = (volumeA.Pressure + volumeB.Pressure) / 2;
        var averageO2 = (volumeA.O2Fraction + volumeB.O2Fraction) / 2;
        var averageTemperature = (volumeA.Temperature + volumeB.Temperature) / 2;

        systemA.ApplyExternalVolume(cellA, volumeA with
        {
            Pressure = Lerp(volumeA.Pressure, averagePressure, factor),
            O2Fraction = Lerp(volumeA.O2Fraction, averageO2, factor),
            Temperature = Lerp(volumeA.Temperature, averageTemperature, factor),
        });

        systemB.ApplyExternalVolume(cellB, volumeB with
        {
            Pressure = Lerp(volumeB.Pressure, averagePressure, factor),
            O2Fraction = Lerp(volumeB.O2Fraction, averageO2, factor),
            Temperature = Lerp(volumeB.Temperature, averageTemperature, factor),
        });
    }

    private static double Lerp(double from, double to, double factor) => from + (to - from) * factor;
}
