using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for BunkVerbTarget's Maintain/Repair verbs — the first fixed-
/// furniture object to get real Deck-tracked wear despite not participating in the power graph
/// at all (see ShipSim.HasBunk, independent of HasPowerGrid).</summary>
[TestSuite]
public class BunkUpkeepTest
{
    private static (BunkVerbTarget Bunk, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipSim = AutoFree(new ShipSim { HasBunk = true });
        sceneTree.Root.AddChild(shipSim);

        var bunk = AutoFree(new BunkVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(bunk);

        return (bunk, shipSim);
    }

    private static float BunkFixtureCondition(ShipSim shipSim) =>
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.BunkFixtureId).Condition;

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_AlwaysOffersSleep_AlongsideMaintainAboveHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bunk, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.BunkFixtureId).Condition = 0.7f;

        AssertBool(bunk.AvailableVerbs.Any(v => v.Id == "sleep")).IsTrue();
        AssertBool(bunk.AvailableVerbs.Any(v => v.Id == "maintain_bunk")).IsTrue();
        AssertBool(bunk.AvailableVerbs.Any(v => v.Id == "repair_bunk")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRepair_AtOrBelowHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bunk, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.BunkFixtureId).Condition = 0.5f;

        AssertBool(bunk.AvailableVerbs.Any(v => v.Id == "repair_bunk")).IsTrue();
        AssertBool(bunk.AvailableVerbs.Any(v => v.Id == "maintain_bunk")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsTheBunksOwnFixtureHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bunk, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.BunkFixtureId).Condition = 0.55f;

        AssertFloat(bunk.Condition!.Value).IsEqual(0.55f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_MaintainBunk_RestoresConditionToFull_WithoutDisturbingSleep()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bunk, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.BunkFixtureId).Condition = 0.6f;

        bunk.ExecuteVerb(new Verb("maintain_bunk", "VERB_MAINTAIN_BUNK", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background, including the ones this await let run.
        AssertFloat(BunkFixtureCondition(shipSim)).IsGreater(0.999f);
        AssertBool(bunk.CurrentVerbProgress is null).IsTrue(); // no stray sleep/maintenance state left running
    }
}
