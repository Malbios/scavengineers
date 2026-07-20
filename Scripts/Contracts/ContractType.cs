namespace Scavengineers.Scripts.Contracts;

/// <summary>The four contract flavors settled on for the mission/contract system — all
/// completable without any AI/NPC crew (docs/project-plan.md's "crew navigation stays grounded"
/// rule doesn't need to apply here since there's no crew at all).</summary>
public enum ContractType
{
    /// <summary>Find a specific item on a specific Derelict, bring it back — turned in at the
    /// ContractGiverVerbTarget (Has → TryRemove → AddCredits, same shape as
    /// VendorVerbTarget.TrySell).</summary>
    RetrieveItem,

    /// <summary>Carry cargo "healthy" from one Station to the other — completes automatically on
    /// arrival at the target Station (see TravelConsoleVerbTarget.ApplyCurrentLocation), not a
    /// turn-in interaction. The "healthy" condition check is a deliberate placeholder that always
    /// passes for now — no real in-transit risk exists yet.</summary>
    CargoDelivery,

    /// <summary>Deliver N units of a material, not one specific item — turned in at the
    /// ContractGiverVerbTarget, same Has/TryRemove/AddCredits shape as RetrieveItem.</summary>
    SalvageQuota,

    /// <summary>Dock at a target with the right PDA scan cartridge equipped — completes
    /// automatically on arrival, same as CargoDelivery, just checking equipment instead of a
    /// carried cargo item.</summary>
    Survey,
}
