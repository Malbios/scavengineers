using System;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The exploration-pressure mechanic (docs/project-plan.md §4): a limited-time budget while
/// away from safety. Constant-rate drain, plus extra O2 drain proportional to how far the
/// surrounding atmosphere has dropped from breathable — a breached room actually costs you,
/// not just a cosmetic backdrop. No consequence at 0% yet (that's a separate, later system).
/// </summary>
public sealed class SuitResources
{
    private const float O2DrainPerSecond = 100f / 300f; // empties over ~5 minutes
    private const float PowerDrainPerSecond = 100f / 480f; // empties over ~8 minutes
    private const float ExposureDrainMultiplier = 100f / 20f; // full vacuum: empties in ~20s on top of the base drain
    private const float FlashlightPowerDrainPerSecond = 100f / 900f; // placeholder/tunable: ~15 min of continuous use
    private const float SmokeExposureDrainPerSecond = 100f / 30f; // placeholder/tunable: empties O2 in ~30s of smoke on top of the base drain
    private const double BreathableO2Fraction = 0.21;

    public float O2Percent { get; private set; } = 100f;

    public float PowerPercent { get; private set; } = 100f;

    public void Tick(double delta, double ambientO2Fraction = BreathableO2Fraction, bool flashlightOn = false, bool inSmoke = false)
    {
        var exposureFraction = Math.Clamp(1 - ambientO2Fraction / BreathableO2Fraction, 0, 1);
        var totalO2Drain = O2DrainPerSecond + ExposureDrainMultiplier * (float)exposureFraction + (inSmoke ? SmokeExposureDrainPerSecond : 0f);
        var totalPowerDrain = PowerDrainPerSecond + (flashlightOn ? FlashlightPowerDrainPerSecond : 0f);

        O2Percent = Math.Clamp(O2Percent - totalO2Drain * (float)delta, 0f, 100f);
        PowerPercent = Math.Clamp(PowerPercent - totalPowerDrain * (float)delta, 0f, 100f);
    }

    public void RestoreFrom(float o2Percent, float powerPercent)
    {
        O2Percent = Math.Clamp(o2Percent, 0f, 100f);
        PowerPercent = Math.Clamp(powerPercent, 0f, 100f);
    }
}
