using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A player-installable ship engine block with its own internal N2 tank — more of these,
/// fueled, means faster travel (see TravelConsoleVerbTarget). Same "finite charge, refilled by
/// consuming an item" shape as <see cref="BatteryVerbTarget"/>'s Recharge, but unlike Battery
/// there can be many of these at once (see ShipBuildTarget's own _placedThrusters), so each
/// instance carries its own fixture id and mounting edge rather than relying on a single fixed
/// ShipSim constant/MachineType slot.
/// </summary>
public partial class ThrusterVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private static readonly Verb RefuelVerb = new("refuel_thruster", "VERB_REFUEL_THRUSTER", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("n2_tank", 1)],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Set by ShipBuildTarget when it spawns this instance — see BatteryVerbTarget's own
    /// BuildTarget for why this is needed (Uninstall/Scrap reachable while aiming at the
    /// thruster's own box, not just bare wall space next to it).</summary>
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    /// <summary>This specific thruster's own Deck fixture id — derived by ShipBuildTarget from
    /// its normalized mounting edge (see ShipBuildTarget.ThrusterFixtureId), since (unlike
    /// Battery/Switch/RechargeStation) there's no single fixed constant to share across
    /// instances.</summary>
    public string FixtureId { get; set; } = "";

    /// <summary>This thruster's own mounting edge, set once at spawn — needed so ExecuteVerb can
    /// hand it back to <see cref="ShipBuildTarget.ExecuteThrusterRemoval"/>, which looks placement
    /// up by edge rather than by fixture id.</summary>
    public CellCoord EdgeA { get; set; }

    public CellCoord EdgeB { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    public string? DisplayNameKey => "OBJECT_THRUSTER";

    /// <summary>The thruster's own N2 charge fraction — excluded from WearSystem's passive decay
    /// (see ThrusterFixture), same as Battery's charge.</summary>
    public float? Condition => ShipSimRef?.ThrusterChargeFraction(FixtureId);

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    // Hidden once already full — same reasoning as BatteryVerbTarget's RechargeVerb gating.
    public IReadOnlyList<Verb> AvailableVerbs =>
        (ShipSimRef is not null && ShipSimRef.ThrusterChargeFraction(FixtureId) < 1f
            ? new List<Verb> { RefuelVerb with { DisplaySuffix = $"{ShipSimRef.ThrusterChargeFraction(FixtureId) * 100:F0}%" } }
            : [])
        .Concat(BuildTarget?.ThrusterRemovalVerbs ?? [])
        .ToList();

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == RefuelVerb.Id)
        {
            ShipSimRef?.SetThrusterCharge(FixtureId, 1f);
            return;
        }

        BuildTarget?.ExecuteThrusterRemoval(EdgeA, EdgeB, verb, inventory);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    public string GetSaveState() =>
        (ShipSimRef?.ThrusterChargeFraction(FixtureId) ?? 1f).ToString(CultureInfo.InvariantCulture);

    public void ApplySaveState(string state)
    {
        if (float.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var charge))
        {
            ShipSimRef?.SetThrusterCharge(FixtureId, charge);
        }
    }
}
