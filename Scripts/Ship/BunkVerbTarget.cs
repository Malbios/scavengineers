using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.ShipModel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Fixed Home Ship furniture (no build-target removal, like TravelConsoleVerbTarget) that
/// restores the player's Energy need (PlayerNeeds, docs/project-plan.md's time-acceleration
/// note) — a plain timed verb, not a power-gated station like RechargeStationVerbTarget, since
/// a bunk doesn't need power.
/// </summary>
public partial class BunkVerbTarget : StaticBody3D, IVerbTarget
{
    // Placeholder/tunable — near-instant for dev testing, same spirit as every other timed verb.
    private static readonly Verb SleepVerb = new("sleep", "VERB_SLEEP", DurationSeconds: 3f);

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see
    // MaintenanceTier) — this bunk's own fixture (ShipSim.BunkFixtureId) decays passively like
    // any other, even though it doesn't participate in the power graph.
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainBunkVerb = new("maintain_bunk", "VERB_MAINTAIN_BUNK", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairBunkVerb = new("repair_bunk", "VERB_REPAIR_BUNK", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    private Timer? _sleepTimer;
    private bool _sleeping;

    // A second, independent timer/bool pair — sleeping and upkeep are two unrelated timed
    // actions on the same object, not worth folding into one dispatch at this scale.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            SleepVerb,
            .. BunkFixture is { } fixture && MaintenanceTier.PickVerb(fixture.Condition, MaintainBunkVerb, RepairBunkVerb) is { } upkeepVerb ? new[] { upkeepVerb } : [],
        ];

    public string? DisplayNameKey => "OBJECT_BUNK";

    private Fixture? BunkFixture => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.BunkFixtureId);

    public float? Condition => BunkFixture?.Condition;

    public float? CurrentVerbProgress =>
        _sleeping ? 1f - (float)(_sleepTimer!.TimeLeft / _sleepTimer.WaitTime)
        : _maintaining ? 1f - (float)(_maintenanceTimer!.TimeLeft / _maintenanceTimer.WaitTime)
        : null;

    public override void _Ready()
    {
        _sleepTimer = new Timer { OneShot = true, WaitTime = SleepVerb.DurationSeconds };
        AddChild(_sleepTimer);
        _sleepTimer.Timeout += OnSleepComplete;

        _maintenanceTimer = new Timer { OneShot = true, WaitTime = MaintainBunkVerb.DurationSeconds };
        AddChild(_maintenanceTimer);
        _maintenanceTimer.Timeout += OnMaintenanceComplete;
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == MaintainBunkVerb.Id || verb.Id == RepairBunkVerb.Id)
        {
            if (!_maintaining)
            {
                _maintaining = true;
                _maintenanceTimer!.Start();
            }

            return;
        }

        if (verb.Id != SleepVerb.Id || _sleeping)
        {
            return;
        }

        _sleeping = true;
        _sleepTimer!.Start();
    }

    public void CancelVerb()
    {
        if (_maintaining)
        {
            _maintaining = false;
            _maintenanceTimer!.Stop();
        }

        if (!_sleeping)
        {
            return;
        }

        _sleeping = false;
        _sleepTimer!.Stop();
    }

    private void OnSleepComplete()
    {
        _sleeping = false;
        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.RestEnergy();
        }
    }

    private void OnMaintenanceComplete()
    {
        _maintaining = false;
        if (BunkFixture is { } fixture)
        {
            fixture.Condition = 1f;
        }
    }
}
