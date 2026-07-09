namespace Scavengineers.Scripts.Interaction;

public interface IInteractable
{
    string InteractionPrompt { get; }

    void Interact();
}
