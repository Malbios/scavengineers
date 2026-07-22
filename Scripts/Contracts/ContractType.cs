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
    /// destination Station and hand it over — turned in at that Station's own ContractGiverVerbTarget,
    /// same Has -> TryRemove -> AddCredits shape as RetrieveItem, but additionally gated on being
    /// at the *right* giver (see Player.CanTurnIn): arriving while still carrying it isn't enough
    /// on its own, and turning in at the wrong Station's board (e.g. the origin) is refused.
    /// Losing the cargo along the way (dropped, or ejected by a hull breach) just means it can
    /// never be handed over — the contract keeps ticking toward its own deadline like any other
    /// unmet contract.</summary>
    CargoDelivery,

    /// <summary>Deliver N units of a material, not one specific item — turned in at the
    /// ContractGiverVerbTarget, same Has/TryRemove/AddCredits shape as RetrieveItem.</summary>
    SalvageQuota,

    /// <summary>Dock at a target with the right PDA scan cartridge equipped — completes
    /// automatically on arrival (see Player.OnArrivedAtDestination), unlike every other type here,
    /// since there's nothing to hand over — just checking equipment.</summary>
    Survey,
}
