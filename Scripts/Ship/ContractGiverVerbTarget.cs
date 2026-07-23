using System;
using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Contracts;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Travel;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>The Station's contract-giver. Rolls a small pool of offers from ContractCatalog's
/// data-driven templates, resolving each offer's real Derelict/Station targets from
/// <see cref="ConsoleRef"/>'s BuildMapEntries — ContractCatalog itself has no notion of the live
/// travel-console state, only item pools/counts/rewards/deadlines.</summary>
public partial class ContractGiverVerbTarget : StaticBody3D, IVerbTarget
{
    // Placeholder/tunable — how many offers sit on the board at once.
    private const int MaxOffers = 3;

    private static readonly Verb OpenContractBoardVerb = new("open_contract_board", "VERB_OPEN_CONTRACT_BOARD", DurationSeconds: 0f);

    [Export]
    public TravelConsoleVerbTarget? ConsoleRef { get; set; }

    private readonly Random _rng = new();
    private readonly List<Contract> _availableOffers = new();

    public string? DisplayNameKey => "OBJECT_CONTRACT_GIVER";

    public float? CurrentVerbProgress => null;

    public IReadOnlyList<Verb> AvailableVerbs => [OpenContractBoardVerb];

    public IReadOnlyList<Contract> AvailableOffers => _availableOffers;

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != OpenContractBoardVerb.Id)
        {
            return;
        }

        RefreshOffers();

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.OpenContractBoard(this);
        }
    }

    public void CancelVerb()
    {
    }

    /// <summary>Tops the board back up to MaxOffers, called every time the board is opened. Stops
    /// early if a template can't currently be resolved into a real offer — e.g. CargoDelivery
    /// needs at least 2 Stations that aren't the current destination — leaving the board under
    /// MaxOffers until the next visit rather than looping forever.</summary>
    public void RefreshOffers()
    {
        while (_availableOffers.Count < MaxOffers)
        {
            var offer = RollOneOffer();
            if (offer is null)
            {
                break;
            }

            _availableOffers.Add(offer);
        }
    }

    /// <summary>Removes and returns the named offer so an accepted job can't be double-accepted.
    /// Null if the id doesn't match any current offer (e.g. a stale UI click after RefreshOffers
    /// already replaced it). For RetrieveItem, also spawns the target item onto the target
    /// Derelict; for CargoDelivery, onto the origin Station — nothing else in the loot pipeline
    /// ever places a contract's item anywhere, so without this the job could never be completed.</summary>
    public Contract? TryTakeOffer(string instanceId)
    {
        var offer = _availableOffers.FirstOrDefault(o => o.InstanceId == instanceId);
        if (offer is not null)
        {
            _availableOffers.Remove(offer);
            SpawnMissionItemIfNeeded(offer);
        }

        return offer;
    }

    private void SpawnMissionItemIfNeeded(Contract offer)
    {
        if (offer.Type == ContractType.RetrieveItem && offer.ItemId is { } retrieveItemId && offer.TargetDestinationId is { } derelictDestinationId)
        {
            ConsoleRef?.GetDerelictBuildTarget(derelictDestinationId)?.SpawnMissionItem(retrieveItemId, offer.Count, _rng);
            return;
        }

        if (offer.Type == ContractType.CargoDelivery && offer.ItemId is { } cargoItemId && offer.OriginStationId is { } originStationId)
        {
            ConsoleRef?.GetStationBuildTarget(originStationId)?.SpawnMissionItem(cargoItemId, offer.Count, _rng);
        }
    }

    /// <summary>Test-only seam: puts a resolved Contract directly onto the board, bypassing
    /// RollOneOffer's randomness, so NodeTests can exercise deterministic accept/turn-in/expiry
    /// flows.</summary>
    public void AddOfferForTests(Contract contract) => _availableOffers.Add(contract);

    private Contract? RollOneOffer()
    {
        if (ConsoleRef is null)
        {
            return null;
        }

        var templates = ContractCatalog.AllTemplates;
        if (templates.Count == 0)
        {
            return null;
        }

        var template = templates[_rng.Next(templates.Count)];
        var rolled = ContractCatalog.Roll(template.Id, _rng);

        // Never the player's current destination — sending them somewhere they already are (or,
        // for CargoDelivery, back to where they're about to depart from) isn't a real job.
        var entries = ConsoleRef.BuildMapEntries().Where(e => !e.IsCurrent).ToList();

        return rolled.Type switch
        {
            ContractType.RetrieveItem => PickDestination(entries, rolled, derelictOnly: true),
            ContractType.Survey => PickDestination(entries, rolled, derelictOnly: false),
            ContractType.CargoDelivery => PickCargoRoute(entries, rolled),
            _ => rolled, // SalvageQuota needs no destination at all.
        };
    }

    private Contract? PickDestination(List<TravelMapEntry> entries, Contract rolled, bool derelictOnly)
    {
        // RetrieveItem only ever targets a Derelict (that's the fiction — a Station isn't
        // somewhere you "find" salvage); Survey can target either.
        var candidates = derelictOnly ? entries.Where(e => e.DisplayNameKey.StartsWith("OBJECT_DERELICT_")).ToList() : entries;

        return candidates.Count == 0 ? null : rolled with { TargetDestinationId = candidates[_rng.Next(candidates.Count)].DestinationId };
    }

    private Contract? PickCargoRoute(List<TravelMapEntry> entries, Contract rolled)
    {
        var stationEntries = entries.Where(e => e.DisplayNameKey.StartsWith("OBJECT_STATION")).ToList();
        if (stationEntries.Count == 0 || ConsoleRef is null)
        {
            return null;
        }

        var destination = stationEntries[_rng.Next(stationEntries.Count)];
        return rolled with { OriginStationId = ConsoleRef.CurrentDestinationId, DestinationStationId = destination.DestinationId };
    }

    /// <summary>Every current offer, always acceptable — the Active list's entries are built by
    /// Player instead, since only it knows whether the inventory currently satisfies a
    /// RetrieveItem/SalvageQuota turn-in.</summary>
    public IReadOnlyList<ContractBoardEntry> BuildAvailableEntries() =>
        _availableOffers.Select(c => new ContractBoardEntry(c.InstanceId, Describe(c), ActionAvailable: true)).ToList();

    /// <summary>"{type}: {details} ({reward}cr, {time}s)" — concatenated parts via Tr() per atomic
    /// piece, not an attempt at grammatically correct full-sentence translation.</summary>
    public string Describe(Contract contract)
    {
        var typeLabel = Tr($"CONTRACT_TYPE_{contract.Type.ToString().ToUpperInvariant()}");
        var itemName = contract.ItemId is { } itemId ? Tr($"ITEM_{itemId.ToUpperInvariant()}") : "";

        var details = contract.Type switch
        {
            ContractType.RetrieveItem => $"{itemName} @ {DestinationName(contract.TargetDestinationId)}",
            ContractType.CargoDelivery => $"{itemName}: {DestinationName(contract.OriginStationId)} -> {DestinationName(contract.DestinationStationId)}",
            ContractType.SalvageQuota => $"{contract.Count}x {itemName}",
            ContractType.Survey => $"{DestinationName(contract.TargetDestinationId)} ({itemName})",
            _ => "",
        };

        return $"{typeLabel}: {details} ({contract.Reward}cr, {Mathf.CeilToInt(contract.RemainingSeconds)}s)";
    }

    private string DestinationName(int? destinationId)
    {
        if (destinationId is not { } id || ConsoleRef is null)
        {
            return "?";
        }

        var entry = ConsoleRef.BuildMapEntries().FirstOrDefault(e => e.DestinationId == id);
        return entry.DisplayNameKey is null ? "?" : Tr(entry.DisplayNameKey);
    }
}
