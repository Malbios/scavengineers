using System.Collections.Generic;
using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Verbs;

public interface IVerbTarget
{
    IReadOnlyList<Verb> AvailableVerbs { get; }

    /// <summary>Null when no verb is currently in progress on this target, else 0..1.</summary>
    float? CurrentVerbProgress { get; }

    /// <summary>The acting inventory is passed through so a target can add items to it
    /// (e.g. a pickup) — requirement checking/deduction already happened in Player before
    /// this is called, so most targets can ignore this parameter.</summary>
    void ExecuteVerb(Verb verb, PlayerInventory inventory);

    /// <summary>Cancels whatever verb is currently in progress on this target, if any,
    /// reverting to its idle state without applying the verb's effect.</summary>
    void CancelVerb();

    /// <summary>Localization key for the target's own name, shown above the verb label so the
    /// player can tell what they're about to repair/pick up/toggle — null if this target
    /// doesn't have (or need) one.</summary>
    string? DisplayNameKey => null;

    /// <summary>0-1 health/wear, shown as a percentage on the crosshair label while the PDA's
    /// health-scan cartridge is active (see Player's scan mode) — null if this target has no
    /// health concept at all (most targets; a Fixture-backed or structural-surface-backed one
    /// overrides this with its real Deck-tracked value, see WearSystem).</summary>
    float? Condition => null;
}
