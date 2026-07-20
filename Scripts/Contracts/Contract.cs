namespace Scavengineers.Scripts.Contracts;

/// <summary>A rolled/accepted contract instance — distinct from ContractCatalog.ContractTemplate
/// (the data-driven, unrolled JSON row): a template describes a *kind* of job with ranges
/// (reward, count, deadline); a Contract is one specific offer/acceptance with those ranges
/// already resolved. Target destination ids (which Derelict/Station) are deliberately left for
/// whoever rolls this (see ContractGiverVerbTarget in Part 3) rather than ContractCatalog itself,
/// since picking a real target needs live access to TravelConsoleVerbTarget's current Derelict/
/// Station counts — data outside what a pure catalog loader should know about.</summary>
public sealed class Contract
{
    public required string InstanceId { get; init; }

    public required string TemplateId { get; init; }

    public required ContractType Type { get; init; }

    /// <summary>The specific item id this contract cares about — RetrieveItem/SalvageQuota only,
    /// null for CargoDelivery/Survey (which don't target one specific catalog item).</summary>
    public string? ItemId { get; init; }

    /// <summary>SalvageQuota's "N units" — meaningless (stays 1) for every other type.</summary>
    public int Count { get; init; } = 1;

    /// <summary>RetrieveItem/Survey's target — a Derelict for RetrieveItem, either a Derelict or
    /// Station for Survey. Null for SalvageQuota (turn-in only, no location) and unused for
    /// CargoDelivery (see OriginStationId/DestinationStationId instead).</summary>
    public int? TargetDestinationId { get; init; }

    /// <summary>CargoDelivery's two Station endpoints — both null for every other type.</summary>
    public int? OriginStationId { get; init; }

    public int? DestinationStationId { get; init; }

    public int Reward { get; init; }

    public int FailureFee { get; init; }

    /// <summary>Ticked down once a second while accepted (Player owns the actual countdown timer)
    /// — expiry with this at or below zero removes the contract and adds FailureFee to
    /// Player.PendingDebt.</summary>
    public float RemainingSeconds { get; set; }
}
