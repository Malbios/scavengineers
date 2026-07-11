using System.Collections.Generic;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Generalized verb per docs/architecture/verbs-and-interaction.md — install/repair/
/// dismantle/etc. all hang off this same shape rather than bespoke per-object interactions.
/// </summary>
public sealed record Verb(string Id, string LocalizationKey, float DurationSeconds)
{
    public IReadOnlyList<ItemRequirement> Requirements { get; init; } = [];

    /// <summary>Optional non-localized text shown in parentheses after the verb's label (e.g. a
    /// trade console verb's price) — plain, not a Tr() key, since it's a runtime value rather
    /// than fixed copy.</summary>
    public string? DisplaySuffix { get; init; }

    /// <summary>True for a verb that's shown and selectable but currently can't succeed (e.g. a
    /// Buy verb you don't have enough credits for) — rendered in red rather than hidden, unlike
    /// the usual Requirements-based affordability gate that hides a verb entirely. Executing a
    /// disabled verb is a no-op (see Player.Interact).</summary>
    public bool Disabled { get; init; }
}

public sealed record ItemRequirement(string ItemId, int Count);
