namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Generalized verb per docs/architecture/verbs-and-interaction.md — install/repair/
/// dismantle/etc. all hang off this same shape rather than bespoke per-object interactions.
/// Requirements aren't modeled yet (no inventory to gate on).
/// </summary>
public sealed record Verb(string Id, string LocalizationKey, float DurationSeconds);
