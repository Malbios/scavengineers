using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// The room's breaker. Flipping the physical switch is always possible — a mechanical toggle
/// needs no power of its own to move — but the bulb only actually lights while the switch
/// fixture itself reads as powered (wired all the way back to the battery, which has charge).
/// <see cref="_switchOn"/> is the switch's own position; <see cref="TargetLight"/>'s visibility
/// is a derived value recomputed every tick, not the source of truth, so it correctly goes dark
/// the moment the battery dies even though nobody touched the switch.
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

    // Starts on, matching the scene's default-visible RoomLight and the switch fixture's own
    // IsOpen=false default (closed = power flows) — the same "starts working" state as before
    // this change, for a switch that's still adjacent enough to the battery not to need a wire.
    private bool _switchOn = true;

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [ToggleVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "OBJECT_LIGHT_SWITCH";

    public override void _PhysicsProcess(double delta) => UpdateLightVisibility();

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != ToggleVerb.Id)
        {
            return;
        }

        _switchOn = !_switchOn;
        ShipSimRef?.SetSwitchOpen(!_switchOn);
        UpdateLightVisibility();
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    public bool GetSaveState() => _switchOn;

    public void ApplySaveState(bool state)
    {
        _switchOn = state;
        ShipSimRef?.SetSwitchOpen(!_switchOn);
        UpdateLightVisibility();
    }

    private void UpdateLightVisibility()
    {
        if (TargetLight is not null)
        {
            TargetLight.Visible = _switchOn && (ShipSimRef?.IsPowered(ShipSim.SwitchFixtureId) ?? false);
        }
    }
}
