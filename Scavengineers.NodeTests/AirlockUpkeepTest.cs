using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for AirlockDoorVerbTarget's Maintain/Repair verbs — its own
/// fixture (PowerFixtureId) has been passively decaying since WearSystem shipped with no way to
/// ever repair it until now (see MaintenanceTier), same gap the travel console had.</summary>
[TestSuite]
public class AirlockUpkeepTest
{
    private static (AirlockDoorVerbTarget Airlock, ShipSim ShipA) MakeHarness(SceneTree sceneTree)
    {
        var shipA = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB, PowerFixtureId = ShipSim.StationAirlockFixtureId });
        sceneTree.Root.AddChild(airlock);

        return (airlock, shipA);
    }

    private static float AirlockFixtureCondition(ShipSim shipSim) =>
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.StationAirlockFixtureId).Condition;

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintain_AboveHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (airlock, shipA) = MakeHarness(sceneTree);
        shipA.Deck.Fixtures.Single(f => f.Id == ShipSim.StationAirlockFixtureId).Condition = 0.7f;

        AssertBool(airlock.AvailableVerbs.Any(v => v.Id == "maintain_airlock")).IsTrue();
        AssertBool(airlock.AvailableVerbs.Any(v => v.Id == "repair_airlock")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRepair_AtOrBelowHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (airlock, shipA) = MakeHarness(sceneTree);
        shipA.Deck.Fixtures.Single(f => f.Id == ShipSim.StationAirlockFixtureId).Condition = 0.4f;

        AssertBool(airlock.AvailableVerbs.Any(v => v.Id == "repair_airlock")).IsTrue();
        AssertBool(airlock.AvailableVerbs.Any(v => v.Id == "maintain_airlock")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsTheAirlocksOwnFixtureHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (airlock, shipA) = MakeHarness(sceneTree);
        shipA.Deck.Fixtures.Single(f => f.Id == ShipSim.StationAirlockFixtureId).Condition = 0.55f;

        AssertFloat(airlock.Condition!.Value).IsEqual(0.55f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_RepairAirlock_RestoresConditionToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (airlock, shipA) = MakeHarness(sceneTree);
        shipA.Deck.Fixtures.Single(f => f.Id == ShipSim.StationAirlockFixtureId).Condition = 0.3f;

        airlock.ExecuteVerb(new Verb("repair_airlock", "VERB_REPAIR_AIRLOCK", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background, including the ones this await let run.
        AssertFloat(AirlockFixtureCondition(shipA)).IsGreater(0.999f);
    }
}
