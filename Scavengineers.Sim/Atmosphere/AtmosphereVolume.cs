namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// Lumped per-volume scalars — atmosphere is never simulated per-tile CFD.
/// See docs/architecture/atmosphere-power-sim.md.
/// </summary>
public sealed record AtmosphereVolume(double Pressure, double O2Fraction, double Temperature)
{
    public static AtmosphereVolume Vacuum { get; } = new(Pressure: 0, O2Fraction: 0, Temperature: 2.7);

    public static AtmosphereVolume Breathable { get; } = new(Pressure: 101.3, O2Fraction: 0.21, Temperature: 293.15);
}
