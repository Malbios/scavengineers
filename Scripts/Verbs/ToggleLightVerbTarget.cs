using System.Collections.Generic;
using Godot;

namespace Scavengineers.Scripts.Verbs;

public partial class ToggleLightVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb ToggleVerb = new("toggle", "VERB_TOGGLE", DurationSeconds: 0f);

    [Export]
    public Light3D? TargetLight { get; set; }

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [ToggleVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public void ExecuteVerb(Verb verb)
    {
        if (verb.Id != ToggleVerb.Id || TargetLight is null)
        {
            return;
        }

        TargetLight.Visible = !TargetLight.Visible;
    }
}
