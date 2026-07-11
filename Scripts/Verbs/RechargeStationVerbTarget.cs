using System.Collections.Generic;
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

    public IReadOnlyList<Verb> AvailableVerbs =>
        ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.RechargeFixtureId)
            ? [RechargeVerb]
            : [];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "OBJECT_RECHARGE_STATION";

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != RechargeVerb.Id)
        {
            return;
        }

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.RefillSuitResources();
        }
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }
}
