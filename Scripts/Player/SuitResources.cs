using System;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The exploration-pressure mechanic (docs/project-plan.md §4): a limited-time budget while
/// away from safety. Constant-rate drain, plus extra O2 drain proportional to how far the
/// surrounding atmosphere has dropped from breathable — a breached room actually costs you,
/// not just a cosmetic backdrop. Once O2 bottoms out, it starts draining Health — and extreme
/// ambient temperature (a breach gone properly cold, or standing in an active fire's heat) drains
/// Health too, compounding with O2 depletion rather than being a separate stat/bar: a vented room
/// is airless *and* freezing at once, one threat, not two independent meters.
/// </summary>
public sealed class SuitResources
{
    private const float O2DrainPerSecond = 100f / 300f; // empties over ~5 minutes
    private const float ExposureDrainMultiplier = 100f / 20f; // full vacuum: empties in ~20s on top of the base drain
    private const float SmokeExposureDrainPerSecond = 100f / 30f; // placeholder/tunable: empties O2 in ~30s of smoke on top of the base drain
    private const float O2HealthDrainPerSecond = 100f / 30f; // placeholder/tunable: 0% O2 alone kills in ~30s
    private const float ColdHealthDrainPerSecond = 100f / 20f; // placeholder/tunable: critical cold alone kills faster than O2 depletion alone
    private const float BurnHealthDrainPerSecond = 100f / 15f; // placeholder/tunable: standing in active fire heat kills fastest of the three
    private const double CriticalColdKelvin = 150.0; // placeholder/tunable: well below breathable (293K), most of the way to vacuum (2.7K) — genuinely freezing, not just chilly
    private const double CriticalHeatKelvin = 400.0; // placeholder/tunable: hot enough to actually burn, not just warm
    private const double BreathableO2Fraction = 0.21;
    private const double BreathableTemperature = 293.15;

    public float O2Percent { get; private set; } = 100f;

    public float HealthPercent { get; private set; } = 100f;

    /// <summary>Whether the ambient temperature was critically cold as of the last Tick — read by
    /// Player.cs to drive a screen overlay, not persisted (recomputed fresh every tick).</summary>
    public bool IsFreezing { get; private set; }

    /// <summary>Whether the ambient temperature was critically hot as of the last Tick — same
    /// non-persisted, overlay-driving shape as <see cref="IsFreezing"/>.</summary>
    public bool IsBurning { get; private set; }

    public void Tick(double delta, double ambientO2Fraction = BreathableO2Fraction, double ambientTemperature = BreathableTemperature, bool inSmoke = false)
    {
        var exposureFraction = Math.Clamp(1 - ambientO2Fraction / BreathableO2Fraction, 0, 1);
        var totalO2Drain = O2DrainPerSecond + ExposureDrainMultiplier * (float)exposureFraction + (inSmoke ? SmokeExposureDrainPerSecond : 0f);

        O2Percent = Math.Clamp(O2Percent - totalO2Drain * (float)delta, 0f, 100f);

        IsFreezing = ambientTemperature <= CriticalColdKelvin;
        IsBurning = ambientTemperature >= CriticalHeatKelvin;

        var healthDrain = 0f;
        if (O2Percent <= 0f)
        {
            healthDrain += O2HealthDrainPerSecond;
        }

        if (IsFreezing)
        {
            healthDrain += ColdHealthDrainPerSecond;
        }

        if (IsBurning)
        {
            healthDrain += BurnHealthDrainPerSecond;
        }

        if (healthDrain > 0f)
        {
            HealthPercent = Math.Clamp(HealthPercent - healthDrain * (float)delta, 0f, 100f);
        }
    }

    public void RestoreFrom(float o2Percent, float healthPercent)
    {
        O2Percent = Math.Clamp(o2Percent, 0f, 100f);
        HealthPercent = Math.Clamp(healthPercent, 0f, 100f);
    }
}
