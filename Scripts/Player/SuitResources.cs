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

    // Placeholder/tunable — while sealed and the filter still has charge, CO2 builds up slowly
    // (fills over ~10 minutes); once the filter's exhausted, the same buildup takes only ~60s.
    private const float Co2RiseWithFilterPerSecond = 100f / 600f;
    private const float Co2RiseWithoutFilterPerSecond = 100f / 60f;

    // Placeholder/tunable — CO2 vents back out fast once the suit's off, same "safety valve"
    // spirit as simply not being sealed anymore.
    private const float Co2VentPerSecond = 100f / 10f;

    // Placeholder/tunable — same order of magnitude as O2HealthDrainPerSecond, since CO2 buildup
    // is a suffocation-adjacent hazard in the same family.
    private const float Co2HealthDrainPerSecond = 100f / 30f;

    // Placeholder/tunable — a dead suit battery (no heating/cooling, no life-support pump) hurts
    // you even in a mild room, but slower than the more acute O2/CO2/cold/burn threats.
    private const float BatteryDepletedHealthDrainPerSecond = 100f / 60f;

    // Placeholder/tunable — stranded with no thruster fuel in a sealed suit; same order of
    // magnitude as the battery-depleted drain, a secondary threat rather than the primary one.
    private const float N2DepletedHealthDrainPerSecond = 100f / 60f;

    public float O2Percent { get; private set; } = 100f;

    public float HealthPercent { get; private set; } = 100f;

    /// <summary>0-100, starts at 0 — the player's own exhaled CO2 building up inside a sealed EVA
    /// suit. Only rises while <see cref="Tick"/>'s <c>suitSealed</c> is true (you only build up
    /// CO2 breathing your own recycled air), and vents back toward 0 the moment you're not sealed
    /// (taking the suit off is CO2's safety valve) — a personal <see cref="SuitResources"/>-level
    /// stat, not an ambient/room-level gas the shared atmosphere solver needs to know about.</summary>
    public float CO2Percent { get; private set; }

    /// <summary>Whether the ambient temperature was critically cold as of the last Tick — read by
    /// Player.cs to drive a screen overlay, not persisted (recomputed fresh every tick).</summary>
    public bool IsFreezing { get; private set; }

    /// <summary>Whether the ambient temperature was critically hot as of the last Tick — same
    /// non-persisted, overlay-driving shape as <see cref="IsFreezing"/>.</summary>
    public bool IsBurning { get; private set; }

    /// <summary>All 5 EVA-suit parameters are additive with defaults so every pre-suit call site
    /// (an un-suited player, or an old test) keeps compiling and behaving exactly as before —
    /// <paramref name="suitSealed"/> defaults false, and the four <c>*Depleted</c> flags default
    /// true (fail-safe: an omitted flag reads as "absent/empty," not "somehow already installed").
    /// All tank/filter/battery *charge* bookkeeping (which slot holds what, draining a tank's own
    /// Charge over time or from sustained thrust) lives in Player.cs/PlayerInventory, exactly
    /// where the drill/flashlight's own charge-draining already does — this class only ever
    /// receives simple booleans/floats each tick and stays 100% headless-testable.</summary>
    public void Tick(double delta, double ambientO2Fraction = BreathableO2Fraction, double ambientTemperature = BreathableTemperature, bool inSmoke = false,
        bool suitSealed = false, bool o2TankDepleted = true, bool n2TankDepleted = true, bool filterDepleted = true, bool batteryDepleted = true)
    {
        // Airtight and fed by a working tank: O2Percent doesn't drain from ambient at all (the
        // tank's own Charge — a separate quantity Player.cs drains — is what's actually being
        // consumed). Once the tank empties, or the suit isn't sealed, this is exactly today's
        // unchanged ambient formula — an un-suited player is unaffected by any of this.
        if (!suitSealed || o2TankDepleted)
        {
            var exposureFraction = Math.Clamp(1 - ambientO2Fraction / BreathableO2Fraction, 0, 1);
            var totalO2Drain = O2DrainPerSecond + ExposureDrainMultiplier * (float)exposureFraction + (inSmoke ? SmokeExposureDrainPerSecond : 0f);
            O2Percent = Math.Clamp(O2Percent - totalO2Drain * (float)delta, 0f, 100f);
        }

        var co2Rate = suitSealed ? (filterDepleted ? Co2RiseWithoutFilterPerSecond : Co2RiseWithFilterPerSecond) : -Co2VentPerSecond;
        CO2Percent = Math.Clamp(CO2Percent + co2Rate * (float)delta, 0f, 100f);

        IsFreezing = ambientTemperature <= CriticalColdKelvin;
        IsBurning = ambientTemperature >= CriticalHeatKelvin;

        // A charged suit battery protects against the ambient cold/burn Health drain (heating/
        // cooling) — note IsFreezing/IsBurning themselves stay ambient-temperature-driven and
        // unchanged, so the overlay still shows even while protected ("it's cold, your suit's
        // handling it" is correct feedback, not a bug).
        var batteryProtecting = suitSealed && !batteryDepleted;

        var healthDrain = 0f;
        if (O2Percent <= 0f)
        {
            healthDrain += O2HealthDrainPerSecond;
        }

        if (CO2Percent >= 100f)
        {
            healthDrain += Co2HealthDrainPerSecond;
        }

        if (IsFreezing && !batteryProtecting)
        {
            healthDrain += ColdHealthDrainPerSecond;
        }

        if (IsBurning && !batteryProtecting)
        {
            healthDrain += BurnHealthDrainPerSecond;
        }

        if (suitSealed && batteryDepleted)
        {
            healthDrain += BatteryDepletedHealthDrainPerSecond;
        }

        if (suitSealed && n2TankDepleted)
        {
            healthDrain += N2DepletedHealthDrainPerSecond;
        }

        if (healthDrain > 0f)
        {
            HealthPercent = Math.Clamp(HealthPercent - healthDrain * (float)delta, 0f, 100f);
        }
    }

    public void RestoreFrom(float o2Percent, float healthPercent, float co2Percent = 0f)
    {
        O2Percent = Math.Clamp(o2Percent, 0f, 100f);
        HealthPercent = Math.Clamp(healthPercent, 0f, 100f);
        CO2Percent = Math.Clamp(co2Percent, 0f, 100f);
    }
}
