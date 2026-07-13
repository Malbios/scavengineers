using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// Links two otherwise-independent <see cref="AtmosphereSystem"/>s (e.g. two docked ships) —
/// deliberately a bolt-on, not a merge of the two ships' connectivity graphs — see
/// docs/architecture/atmosphere-power-sim.md.
///
/// Exchanges only the two named cells directly — the airlock is just one more diffusion-style
/// edge, a single hop linking two otherwise-independent graphs. Each side's own
/// <see cref="AtmosphereSystem"/> internal per-cell diffusion carries the effect further into
/// that ship on subsequent ticks, the same way it would for a breach opening at that cell —
/// no need to pool/average each side's entire connected component the way this used to.
/// </summary>
public sealed class AirlockBridge(AtmosphereSystem systemA, CellCoord cellA, AtmosphereSystem systemB, CellCoord cellB)
{
    // Placeholder/tunable — deliberately matches AtmosphereSystem's own VentRatePerSecond: once
    // the airlock is open, any path to outer space should feel dangerous immediately, not just
    // for the room with the actual hole. A slower bridge rate was tried first (treating the
    // airlock as a narrow chokepoint) so a quick transit wouldn't cost much air, but that's the
    // wrong tradeoff for this game — opening an airlock into a breached room should rapidly vent
    // the connected room too, matching the breach's own speed (see AirlockBridgeTests).
    private const double EqualizeRatePerSecond = 5.0;

    public bool IsOpen { get; set; }

    public void Tick(double dt)
    {
        if (!IsOpen)
        {
            return;
        }

        var volumeA = systemA.VolumeAt(cellA);
        var volumeB = systemB.VolumeAt(cellB);

        var averagePressure = (volumeA.Pressure + volumeB.Pressure) / 2;
        var averageO2 = (volumeA.O2Fraction + volumeB.O2Fraction) / 2;
        var averageTemperature = (volumeA.Temperature + volumeB.Temperature) / 2;

        var factor = Math.Clamp(EqualizeRatePerSecond * dt, 0, 1);

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
