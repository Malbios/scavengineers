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

    /// <summary>Carry a real cargo item (spawned at the origin Station on acceptance) to the
    /// destination Station — completes automatically on arrival (see
    /// Player.OnArrivedAtDestination), not a turn-in interaction, but only if the item is still
    /// actually being carried at that moment (Has -> TryRemove, same shape RetrieveItem's turn-in
    /// uses). Losing the cargo along the way (dropped, or ejected by a hull breach) just means it
    /// doesn't complete on that arrival — the contract keeps ticking toward its own deadline like
    /// any other unmet contract.</summary>
    CargoDelivery,

    /// <summary>Deliver N units of a material, not one specific item — turned in at the
    /// ContractGiverVerbTarget, same Has/TryRemove/AddCredits shape as RetrieveItem.</summary>
    SalvageQuota,

    /// <summary>Dock at a target with the right PDA scan cartridge equipped — completes
    /// automatically on arrival, same as CargoDelivery, just checking equipment instead of a
    /// carried cargo item.</summary>
    Survey,
}
