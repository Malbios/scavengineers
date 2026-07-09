using System;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The exploration-pressure mechanic (docs/project-plan.md §4): a limited-time budget while
/// away from safety. Constant-rate drain only for now — no tie-in to ambient atmosphere, no
/// refill, no consequence at 0% (those are separate, later systems).
/// </summary>
public sealed class SuitResources
{
    private const float O2DrainPerSecond = 100f / 300f; // empties over ~5 minutes
    private const float PowerDrainPerSecond = 100f / 480f; // empties over ~8 minutes

    public float O2Percent { get; private set; } = 100f;

    public float PowerPercent { get; private set; } = 100f;

    public void Tick(double delta)
    {
        O2Percent = Math.Clamp(O2Percent - O2DrainPerSecond * (float)delta, 0f, 100f);
        PowerPercent = Math.Clamp(PowerPercent - PowerDrainPerSecond * (float)delta, 0f, 100f);
    }

    public void RestoreFrom(float o2Percent, float powerPercent)
    {
        O2Percent = Math.Clamp(o2Percent, 0f, 100f);
        PowerPercent = Math.Clamp(powerPercent, 0f, 100f);
    }
}
