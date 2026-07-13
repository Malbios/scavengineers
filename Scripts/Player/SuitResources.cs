using System;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The exploration-pressure mechanic (docs/project-plan.md §4): a limited-time budget while
/// away from safety. Constant-rate drain, plus extra O2 drain proportional to how far the
/// surrounding atmosphere has dropped from breathable — a breached room actually costs you,
/// not just a cosmetic backdrop. Once O2 bottoms out, it starts draining Health instead — a real
/// consequence rather than a stat that just sits at 0.
/// </summary>
public sealed class SuitResources
{
    private const float O2DrainPerSecond = 100f / 300f; // empties over ~5 minutes
    private const float ExposureDrainMultiplier = 100f / 20f; // full vacuum: empties in ~20s on top of the base drain
    private const float SmokeExposureDrainPerSecond = 100f / 30f; // placeholder/tunable: empties O2 in ~30s of smoke on top of the base drain
    private const float HealthDrainPerSecond = 100f / 30f; // placeholder/tunable: 0% O2 kills in ~30s
    private const double BreathableO2Fraction = 0.21;

    public float O2Percent { get; private set; } = 100f;

    public float HealthPercent { get; private set; } = 100f;

    public void Tick(double delta, double ambientO2Fraction = BreathableO2Fraction, bool inSmoke = false)
    {
        var exposureFraction = Math.Clamp(1 - ambientO2Fraction / BreathableO2Fraction, 0, 1);
        var totalO2Drain = O2DrainPerSecond + ExposureDrainMultiplier * (float)exposureFraction + (inSmoke ? SmokeExposureDrainPerSecond : 0f);

        O2Percent = Math.Clamp(O2Percent - totalO2Drain * (float)delta, 0f, 100f);

        if (O2Percent <= 0f)
        {
            HealthPercent = Math.Clamp(HealthPercent - HealthDrainPerSecond * (float)delta, 0f, 100f);
        }
    }

    public void RestoreFrom(float o2Percent, float healthPercent)
    {
        O2Percent = Math.Clamp(o2Percent, 0f, 100f);
        HealthPercent = Math.Clamp(healthPercent, 0f, 100f);
    }
}
