using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// The room's breaker. Toggling flips the visible light and the sim's switch fixture
/// together — the light is no longer purely cosmetic, since the same switch also gates
/// power to the recharge station (docs/architecture/atmosphere-power-sim.md's "an open
/// switch cuts power to a region", finally reachable from actual gameplay).
/// </summary>
public partial class ToggleLightVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb ToggleVerb = new("toggle", "VERB_TOGGLE", DurationSeconds: 0f);

    [Export]
    public Light3D? TargetLight { get; set; }

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [ToggleVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "OBJECT_LIGHT_SWITCH";

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != ToggleVerb.Id || TargetLight is null)
        {
            return;
        }

        TargetLight.Visible = !TargetLight.Visible;
        ShipSimRef?.SetSwitchOpen(!TargetLight.Visible);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    public bool GetSaveState() => TargetLight?.Visible ?? false;

    public void ApplySaveState(bool state)
    {
        if (TargetLight is not null)
        {
            TargetLight.Visible = state;
        }

        ShipSimRef?.SetSwitchOpen(!state);
    }
}
