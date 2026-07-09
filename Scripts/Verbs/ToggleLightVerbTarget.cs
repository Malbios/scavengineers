using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;

namespace Scavengineers.Scripts.Verbs;

public partial class ToggleLightVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb ToggleVerb = new("toggle", "VERB_TOGGLE", DurationSeconds: 0f);

    [Export]
    public Light3D? TargetLight { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [ToggleVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != ToggleVerb.Id || TargetLight is null)
        {
            return;
        }

        TargetLight.Visible = !TargetLight.Visible;
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
    }
}
