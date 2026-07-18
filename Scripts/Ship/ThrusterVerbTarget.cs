using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A player-installable ship engine block with its own internal N2 tank — more of these,
/// fueled, means faster travel (see TravelConsoleVerbTarget). Unlike Battery there can be many of
/// these at once (see ShipBuildTarget's own _placedThrusters), so each instance carries its own
/// fixture id and mounting edge rather than relying on a single fixed ShipSim constant/MachineType
/// slot. Fueling is a physical dock, not an instant top-off: a real n2_tank item sits in <see
/// cref="Contents"/> and continuously feeds the fixture's own charge (see _PhysicsProcess) until
/// the tank itself runs dry — the same "swap the empty one for a full one" loop the EVA suit's own
/// N2 tank slot already uses, just world-object-owned instead of player-owned.
/// </summary>
public partial class ThrusterVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private static readonly Verb OpenThrusterVerb = new("open_thruster", "VERB_OPEN_THRUSTER", DurationSeconds: 0f);

    // Placeholder/tunable — independent of TravelConsoleVerbTarget's own ThrusterDrainPerSecond,
    // so refueling isn't tied 1:1 to travel drain.
    private const float TankTransferPerSecond = 0.05f;

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

    /// <summary>The docked N2 tank, if any — a single-slot SlotContainer rather than a new
    /// primitive, so the existing generic drag-and-drop (InventorySlotUI.Container/
    /// SlotContainer.MoveBetween) works with zero new mechanism. Any item can technically be
    /// dropped in here (nothing enforces "n2_tank only," matching how ordinary backpack slots
    /// don't restrict item type either); _PhysicsProcess below simply ignores anything that isn't
    /// a charged n2_tank.</summary>
    public SlotContainer Contents { get; } = new(1);

    [Export]
    public string SaveId { get; set; } = "";

    public string? DisplayNameKey => "OBJECT_THRUSTER";

    /// <summary>The thruster's own N2 charge fraction — excluded from WearSystem's passive decay
    /// (see ThrusterFixture), same as Battery's charge.</summary>
    public float? Condition => ShipSimRef?.ThrusterChargeFraction(FixtureId);

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public IReadOnlyList<Verb> AvailableVerbs =>
        new List<Verb> { OpenThrusterVerb }
            .Concat(BuildTarget?.ThrusterRemovalVerbs ?? [])
            .ToList();

    public override void _PhysicsProcess(double delta)
    {
        if (ShipSimRef is null || Contents.Slots[0] is not { ItemId: "n2_tank" } tank || tank.Charge <= 0f)
        {
            return;
        }

        if (ShipSimRef.ThrusterChargeFraction(FixtureId) >= 1f)
        {
            return;
        }

        var transferred = Mathf.Min(TankTransferPerSecond * (float)delta, tank.Charge);
        ShipSimRef.RechargeThruster(FixtureId, transferred);
        Contents.SetSlot(0, (tank.ItemId, tank.Count, tank.Charge - transferred));
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == OpenThrusterVerb.Id)
        {
            if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
            {
                player.OpenThrusterInventory(this);
            }

            return;
        }

        BuildTarget?.ExecuteThrusterRemoval(EdgeA, EdgeB, verb, inventory);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    /// <summary>Condition alone, or condition plus the docked tank's own remaining charge
    /// (pipe-delimited) — still a single string in MachineCoord.State, no BuildTargetSaveData
    /// schema change needed just because a thruster now has its own sub-item.</summary>
    public string GetSaveState()
    {
        var condition = (ShipSimRef?.ThrusterChargeFraction(FixtureId) ?? 1f).ToString(CultureInfo.InvariantCulture);
        return Contents.Slots[0] is { } tank
            ? $"{condition}|{tank.ItemId}|{tank.Charge.ToString(CultureInfo.InvariantCulture)}"
            : condition;
    }

    public void ApplySaveState(string state)
    {
        var parts = state.Split('|');
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var condition))
        {
            ShipSimRef?.SetThrusterCharge(FixtureId, condition);
        }

        if (parts.Length >= 3 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var tankCharge))
        {
            Contents.SetSlot(0, (parts[1], 1, tankCharge));
        }
    }
}
