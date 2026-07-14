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
///
/// Once either side already has its own path to Outside (its own hull breach reachable through
/// its own diffusion graph — see <see cref="AtmosphereSystem.IsConnectedToOutside"/>), the bridge
/// stops averaging the two cells and instead pulls both straight toward
/// <see cref="AtmosphereVolume.Vacuum"/>, same rate, same target <see
/// cref="AtmosphereSystem"/>'s own Vent uses. Averaging alone only pulls each side toward the
/// *other's current value* — if the actual breach is several diffusion-hops from the airlock,
/// its full strength never reaches the doorway, and a regenerating source room can hold the
/// shared average at a deceptively "safe" plateau indefinitely (confirmed via a throwaway probe
/// before this fix — see AirlockBridgeTests). Pulling both sides toward the same fixed target,
/// not a moving average, is what actually reproduces "the whole thing depressurizes together,"
/// matching a real breach venting anything feeding it, source included. It also marks each
/// system's drained cell via <see cref="AtmosphereSystem.MarkExternallyVented"/>, so that side's
/// own life-support regen (if it has any) doesn't fight the drain — a whole-ship regen can
/// otherwise out-compete this single-point pull purely through dilution once the ship is bigger
/// than one cell (confirmed via a second throwaway probe at real ship scale — see
/// AirlockBridgeTests).
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
        var factor = Math.Clamp(EqualizeRatePerSecond * dt, 0, 1);

        if (systemA.IsConnectedToOutside(cellA) || systemB.IsConnectedToOutside(cellB))
        {
            systemA.ApplyExternalVolume(cellA, volumeA with
            {
                Pressure = Lerp(volumeA.Pressure, AtmosphereVolume.Vacuum.Pressure, factor),
                O2Fraction = Lerp(volumeA.O2Fraction, AtmosphereVolume.Vacuum.O2Fraction, factor),
                Temperature = Lerp(volumeA.Temperature, AtmosphereVolume.Vacuum.Temperature, factor),
            });

            systemB.ApplyExternalVolume(cellB, volumeB with
            {
                Pressure = Lerp(volumeB.Pressure, AtmosphereVolume.Vacuum.Pressure, factor),
                O2Fraction = Lerp(volumeB.O2Fraction, AtmosphereVolume.Vacuum.O2Fraction, factor),
                Temperature = Lerp(volumeB.Temperature, AtmosphereVolume.Vacuum.Temperature, factor),
            });

            systemA.MarkExternallyVented(cellA);
            systemB.MarkExternallyVented(cellB);

            return;
        }

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
