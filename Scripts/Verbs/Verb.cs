using System.Collections.Generic;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Generalized verb per docs/architecture/verbs-and-interaction.md — install/repair/
/// dismantle/etc. all hang off this same shape rather than bespoke per-object interactions.
/// </summary>
public sealed record Verb(string Id, string LocalizationKey, float DurationSeconds)
{
    public IReadOnlyList<ItemRequirement> Requirements { get; init; } = [];
}

public sealed record ItemRequirement(string ItemId, int Count);
