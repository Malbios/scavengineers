using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for TravelConsoleVerbTarget's Maintain/Repair verbs — its own
/// fixture (ShipSim.TravelConsoleFixtureId) has been passively decaying since WearSystem shipped
/// with no way to ever repair it until now (see MaintenanceTier).</summary>
[TestSuite]
public class TravelConsoleUpkeepTest
{
    private static (TravelConsoleVerbTarget Console, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipSim = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(shipSim);

        var console = AutoFree(new TravelConsoleVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(console);

        return (console, shipSim);
    }

    private static float ConsoleFixtureCondition(ShipSim shipSim) =>
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId).Condition;

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintain_AboveHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId).Condition = 0.7f;

        AssertBool(console.AvailableVerbs.Any(v => v.Id == "maintain_travel_console")).IsTrue();
        AssertBool(console.AvailableVerbs.Any(v => v.Id == "repair_travel_console")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRepair_AtOrBelowHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId).Condition = 0.4f;

        AssertBool(console.AvailableVerbs.Any(v => v.Id == "repair_travel_console")).IsTrue();
        AssertBool(console.AvailableVerbs.Any(v => v.Id == "maintain_travel_console")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_StillOffersUpkeep_EvenWhileUnpowered()
    {
        // Nothing wires this fixture to a battery in this harness, so IsPowered is false — the
        // Travel verb itself should be absent, but upkeep must still be offered: a console you
        // can't currently use to travel should still be repairable.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId).Condition = 0.4f;

        AssertBool(console.AvailableVerbs.Any(v => v.Id == "travel")).IsFalse();
        AssertBool(console.AvailableVerbs.Any(v => v.Id == "repair_travel_console")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_RepairTravelConsole_RestoresConditionToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId).Condition = 0.3f;

        console.ExecuteVerb(new Verb("repair_travel_console", "VERB_REPAIR_TRAVEL_CONSOLE", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background, including the ones this await let run.
        AssertFloat(ConsoleFixtureCondition(shipSim)).IsGreater(0.999f);
    }
}
