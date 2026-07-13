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
    // Placeholder/tunable — deliberately much slower than any within-deck rate (Equalize is
    // instant, AtmosphereSystem's own VentRatePerSecond is 5.0): a real airlock doorway is a
    // narrow chokepoint, not an open connection between two rooms. At the old 0.5 (equal to
    // AtmosphereSystem's vent rate), a home-ship room bridged to an aggressively-vented breached
    // room got dragged toward the same near-vacuum shared average almost as fast as the breached
    // room itself vented — a normal few-second airlock transit already cost a third of the room's
    // air, and leaving the airlock open half a minute fully drained it. This still lets a
    // long-left-open airlock gradually drain a connected room (real tension), just on a timescale
    // of tens of seconds rather than single-digit ones (see AirlockBridgeTests).
    private const double EqualizeRatePerSecond = 0.05;

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
