using Godot;

namespace Scavengineers.Scripts.Interaction;

public partial class ToggleLightInteractable : StaticBody3D, IInteractable
{
    [Export]
    public Light3D? TargetLight { get; set; }

    public string InteractionPrompt => Tr("INTERACTION_TOGGLE_LIGHT");

    public void Interact()
    {
        if (TargetLight is not null)
        {
            TargetLight.Visible = !TargetLight.Visible;
        }
    }
}
