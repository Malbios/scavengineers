using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Refills the player's suit O2/power, but only while its ship's recharge-station fixture
/// is powered — cutting the breaker (<see cref="ToggleLightVerbTarget"/>) really does cut
/// this off, giving the power grid an upkeep stake beyond a cosmetic light.
/// </summary>
public partial class RechargeStationVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb RechargeVerb = new("recharge", "VERB_RECHARGE", DurationSeconds: 0.2f);

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Set by ShipBuildTarget when it spawns this instance — see BatteryVerbTarget's own
    /// BuildTarget for why this is needed (Uninstall/Scrap reachable while aiming at the station's
    /// own box, not just bare wall space next to it — including while it's unpowered, since
    /// Recharge alone would otherwise vanish entirely and leave nothing to aim at).</summary>
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            .. ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.RechargeFixtureId) ? new[] { RechargeVerb } : [],
            .. BuildTarget?.MachineMaintainRepairVerbs(ShipBuildTarget.MachineType.RechargeStation) ?? [],
            .. BuildTarget?.MachineRemovalVerbs(ShipBuildTarget.MachineType.RechargeStation) ?? [],
        ];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "OBJECT_RECHARGE_STATION";

    public float? Condition => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.RechargeFixtureId)?.Condition;

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == RechargeVerb.Id)
        {
            if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
            {
                player.RefillSuitResources();
            }

            return;
        }

        BuildTarget?.ExecuteMachineMaintainRepair(ShipBuildTarget.MachineType.RechargeStation, verb, inventory);
        BuildTarget?.ExecuteMachineRemoval(ShipBuildTarget.MachineType.RechargeStation, verb, inventory);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }
}
