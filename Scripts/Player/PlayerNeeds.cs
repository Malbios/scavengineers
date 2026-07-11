using System;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The player's own long-horizon survival needs (hunger, thirst, energy) — distinct from
/// <see cref="SuitResources"/>'s short-horizon EVA budget (O2/power drain over minutes; these
/// drain over a much longer span, matching the home-ship-loop pressure docs/project-plan.md's
/// time-acceleration note already anticipates: "fatigue, hunger, suit O2, power all tick").
/// Constant-rate drain only, no consequence at 0% yet — same "later system" deferral
/// SuitResources' own O2/power already use.
/// </summary>
public sealed class PlayerNeeds
{
    private const float HungerDrainPerSecond = 100f / 1200f; // placeholder/tunable: empties over ~20 minutes
    private const float ThirstDrainPerSecond = 100f / 900f; // placeholder/tunable: empties over ~15 minutes
    private const float EnergyDrainPerSecond = 100f / 1500f; // placeholder/tunable: empties over ~25 minutes

    public float HungerPercent { get; private set; } = 100f;

    public float ThirstPercent { get; private set; } = 100f;

    public float EnergyPercent { get; private set; } = 100f;

    public void Tick(double delta)
    {
        HungerPercent = Math.Clamp(HungerPercent - HungerDrainPerSecond * (float)delta, 0f, 100f);
        ThirstPercent = Math.Clamp(ThirstPercent - ThirstDrainPerSecond * (float)delta, 0f, 100f);
        EnergyPercent = Math.Clamp(EnergyPercent - EnergyDrainPerSecond * (float)delta, 0f, 100f);
    }

    public void Eat(float amount) => HungerPercent = Math.Clamp(HungerPercent + amount, 0f, 100f);

    public void Drink(float amount) => ThirstPercent = Math.Clamp(ThirstPercent + amount, 0f, 100f);

    public void Rest(float amount) => EnergyPercent = Math.Clamp(EnergyPercent + amount, 0f, 100f);

    public void RestoreFrom(float hungerPercent, float thirstPercent, float energyPercent)
    {
        HungerPercent = Math.Clamp(hungerPercent, 0f, 100f);
        ThirstPercent = Math.Clamp(thirstPercent, 0f, 100f);
        EnergyPercent = Math.Clamp(energyPercent, 0f, 100f);
    }
}
