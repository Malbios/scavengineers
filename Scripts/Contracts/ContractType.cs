namespace Scavengineers.Scripts.Contracts;

/// <summary>The four contract flavors settled on for the mission/contract system — all
/// completable without any AI/NPC crew.</summary>
public enum ContractType
{
    /// <summary>Find a specific item on a specific Derelict, bring it back — turned in at the
    /// ContractGiverVerbTarget (Has → TryRemove → AddCredits, same shape as
    /// VendorVerbTarget.TrySell).</summary>
    RetrieveItem,

    /// <summary>Carry a real cargo item (spawned at the origin Station on acceptance) to the
    /// destination Station and hand it over — additionally gated on being at the *right* giver
    /// (see Player.CanTurnIn): arriving while still carrying it isn't enough, and turning in at
    /// the wrong Station's board is refused. Losing the cargo along the way just means it can
    /// never be handed over — the contract keeps ticking toward its own deadline.</summary>
    CargoDelivery,

    /// <summary>Deliver N units of a material, not one specific item — same Has/TryRemove/
    /// AddCredits shape as RetrieveItem.</summary>
    SalvageQuota,

    /// <summary>Dock at a target with the right PDA scan cartridge equipped — completes
    /// automatically on arrival, unlike every other type here, since there's nothing to hand
    /// over.</summary>
    Survey,
}
