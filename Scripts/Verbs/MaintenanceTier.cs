namespace Scavengineers.Scripts.Verbs;

/// <summary>Shared Ostranauts-style two-tier maintenance rule (mechanic only, not the name/text —
/// see this repo's own "spiritual successor" constraint): full health offers nothing, above 50%
/// offers a free (tool-only) Maintain, at or below 50% offers a Repair that also consumes a
/// resource. Used by ShipBuildTarget (structural surfaces, conduits, Switch/RechargeStation) and
/// any other fixed VerbTarget with its own Deck-tracked wear (Bunk, TravelConsole).</summary>
public static class MaintenanceTier
{
    public static Verb? PickVerb(float health, Verb maintainVerb, Verb repairVerb) =>
        health >= 1f ? null : health > 0.5f ? maintainVerb : repairVerb;
}
