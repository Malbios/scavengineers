using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// Links two otherwise-independent <see cref="AtmosphereSystem"/>s (e.g. two docked ships) —
/// deliberately a bolt-on, not a merge of the two ships' connectivity graphs — see
/// docs/architecture/atmosphere-power-sim.md. Reuses the same lumped-scalar equalization idea
/// <see cref="AtmosphereSystem"/> already applies within a single deck, just gradually (via a
/// rate) instead of instantly, since the two sides are only linked through an airlock.
///
/// Averages each side's *entire currently-connected volume* (via
/// <see cref="AtmosphereSystem.ComponentContaining"/>), not just the two named cells — a real
/// airlock joins two whole rooms, not two points, and with either ship now a multi-cell grid,
/// nudging one cell alone gets diluted away by that ship's own internal equalize before it
/// could ever show up.
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

        var cellsA = systemA.ComponentContaining(cellA);
        var cellsB = systemB.ComponentContaining(cellB);

        var volumesA = cellsA.Select(systemA.VolumeAt).ToList();
        var volumesB = cellsB.Select(systemB.VolumeAt).ToList();
        var allVolumes = volumesA.Concat(volumesB).ToList();

        var averagePressure = allVolumes.Average(v => v.Pressure);
        var averageO2 = allVolumes.Average(v => v.O2Fraction);
        var averageTemperature = allVolumes.Average(v => v.Temperature);

        var factor = Math.Clamp(EqualizeRatePerSecond * dt, 0, 1);

        foreach (var cell in cellsA)
        {
            var current = systemA.VolumeAt(cell);
            systemA.ApplyExternalVolume(cell, current with
            {
                Pressure = Lerp(current.Pressure, averagePressure, factor),
                O2Fraction = Lerp(current.O2Fraction, averageO2, factor),
                Temperature = Lerp(current.Temperature, averageTemperature, factor),
            });
        }

        foreach (var cell in cellsB)
        {
            var current = systemB.VolumeAt(cell);
            systemB.ApplyExternalVolume(cell, current with
            {
                Pressure = Lerp(current.Pressure, averagePressure, factor),
                O2Fraction = Lerp(current.O2Fraction, averageO2, factor),
                Temperature = Lerp(current.Temperature, averageTemperature, factor),
            });
        }
    }

    private static double Lerp(double from, double to, double factor) => from + (to - from) * factor;
}
