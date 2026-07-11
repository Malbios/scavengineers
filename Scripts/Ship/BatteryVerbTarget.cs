using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Home Ship's sole power source (docs/project-plan.md §4 step 6 — "recharge cells" made
/// real). Holds a finite charge (see <see cref="ShipSim.BatteryChargeFraction"/>) that every
/// wired-up consumer's <see cref="ShipSim.IsPowered"/> check depends on. Recharging costs a
/// purchasable power_cell item, mirroring how DamagedConduitVerbTarget's Repair consumes
/// spare_parts.
/// </summary>
public partial class BatteryVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private static readonly Verb RechargeVerb = new("recharge_battery", "VERB_RECHARGE_BATTERY", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("power_cell", 1)],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Set by ShipBuildTarget when it spawns this instance — this is how Uninstall/Scrap
    /// (owned by ShipBuildTarget, since installing/removing a machine is really a wall-mount
    /// action) reach the player while aiming directly at the battery's own box, not just at bare
    /// wall space next to it.</summary>
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    public string? DisplayNameKey => "OBJECT_BATTERY";

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    // Hidden once already full — mirrors SellVerbs only showing while there's something to
    // sell, rather than always offering a no-op top-off. Uninstall/Scrap always tag along
    // regardless of charge — you can remove a full battery just as easily as an empty one.
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
