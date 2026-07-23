using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>The Home Ship's sole power source. Holds a finite charge (see
/// <see cref="ShipSim.BatteryChargeFraction"/>) that every wired-up consumer's
/// <see cref="ShipSim.IsPowered"/> check depends on. Recharging costs a purchasable power_cell
/// item.</summary>
public partial class BatteryVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private static readonly Verb RechargeVerb = new("recharge_battery", "VERB_RECHARGE_BATTERY", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("power_cell", 1)],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Set by ShipBuildTarget when it spawns this instance — this is how Uninstall/Scrap
    /// reach the player while aiming directly at the battery's own box.</summary>
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    public string? DisplayNameKey => "OBJECT_BATTERY";

    /// <summary>Charge, not wear — excluded from WearSystem's passive decay. No Maintain/Repair
    /// verbs here: charge already has its own Recharge verb + drain mechanic.</summary>
    public float? Condition => ShipSimRef?.BatteryChargeFraction;

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    // Hidden once already full. Uninstall/Scrap always tag along regardless of charge.
    public IReadOnlyList<Verb> AvailableVerbs =>
        (ShipSimRef is not null && ShipSimRef.BatteryChargeFraction < 1f
            ? new List<Verb> { RechargeVerb with { DisplaySuffix = $"{ShipSimRef.BatteryChargeFraction * 100:F0}%" } }
            : [])
        .Concat(BuildTarget?.MachineRemovalVerbs(ShipBuildTarget.MachineType.Battery) ?? [])
        .ToList();

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == RechargeVerb.Id)
        {
            ShipSimRef?.RechargeBattery(ShipSim.PowerCellRechargeAmount);
            return;
        }

        BuildTarget?.ExecuteMachineRemoval(ShipBuildTarget.MachineType.Battery, verb, inventory);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    public string GetSaveState() =>
        (ShipSimRef?.BatteryChargeFraction ?? 1f).ToString(CultureInfo.InvariantCulture);

    public void ApplySaveState(string state)
    {
        if (float.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var charge))
        {
            ShipSimRef?.SetBatteryCharge(charge);
        }
    }
}
