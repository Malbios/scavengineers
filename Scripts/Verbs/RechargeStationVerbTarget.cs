using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.ShipModel;
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

    // Placeholder/tunable — Recharge is a near-instant 0.2s action with no other "in progress"
    // signal to hook a sustained draw off (ExecuteVerb only fires once Player's own generic
    // verb-duration wait has already elapsed), so this is a brief, honest power spike after the
    // fact rather than pretending Recharge is a multi-second continuous action it isn't.
    private const float ActiveDrawWindowSeconds = 1.5f;

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    private Timer? _activeDrawTimer;

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            .. ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.RechargeFixtureId) ? new[] { RechargeVerb } : [],
            .. BuildTarget?.MachineMaintainRepairVerbs(ShipBuildTarget.MachineType.RechargeStation) ?? [],
            .. BuildTarget?.MachineRemovalVerbs(ShipBuildTarget.MachineType.RechargeStation) ?? [],
        ];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "OBJECT_RECHARGE_STATION";

    private Fixture? RechargeFixture => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.RechargeFixtureId);

    public float? Condition => RechargeFixture?.Condition;

    public override void _Ready()
    {
        _activeDrawTimer = new Timer { OneShot = true, WaitTime = ActiveDrawWindowSeconds };
        AddChild(_activeDrawTimer);
        _activeDrawTimer.Timeout += () =>
        {
            if (RechargeFixture is { } fixture)
            {
                fixture.PowerDraw = ShipSim.IdleDraw;
            }
        };
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == RechargeVerb.Id)
        {
            if (RechargeFixture is { } fixture)
            {
                fixture.PowerDraw = ShipSim.RechargeStationActiveDraw;
            }

            _activeDrawTimer!.Start(); // restarts the window if already running

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
