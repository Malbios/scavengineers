using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for InteriorDoorVerbTarget's Maintain/Repair verbs — its own
/// fixture (ShipSim.InteriorDoorFixtureId) used to only exist on a ship with HasPowerGrid (so a
/// Derelict's own interior door never had one at all, and even the Home Ship's only half-worked),
/// now created unconditionally for every ship (see ShipSim._Ready()).</summary>
[TestSuite]
public class InteriorDoorUpkeepTest
{
    private static (InteriorDoorVerbTarget Door, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(door);

        return (door, shipSim);
    }

    private static float DoorFixtureCondition(ShipSim shipSim) =>
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.InteriorDoorFixtureId).Condition;

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintain_AboveHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (door, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.InteriorDoorFixtureId).Condition = 0.7f;

        AssertBool(door.AvailableVerbs.Any(v => v.Id == "maintain_interior_door")).IsTrue();
        AssertBool(door.AvailableVerbs.Any(v => v.Id == "repair_interior_door")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRepair_AtOrBelowHalfHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (door, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.InteriorDoorFixtureId).Condition = 0.4f;

        AssertBool(door.AvailableVerbs.Any(v => v.Id == "repair_interior_door")).IsTrue();
        AssertBool(door.AvailableVerbs.Any(v => v.Id == "maintain_interior_door")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsTheDoorsOwnFixtureHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (door, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.InteriorDoorFixtureId).Condition = 0.55f;

        AssertFloat(door.Condition!.Value).IsEqual(0.55f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_RepairInteriorDoor_RestoresConditionToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (door, shipSim) = MakeHarness(sceneTree);
        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.InteriorDoorFixtureId).Condition = 0.3f;

        door.ExecuteVerb(new Verb("repair_interior_door", "VERB_REPAIR_INTERIOR_DOOR", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background, including the ones this await let run.
        AssertFloat(DoorFixtureCondition(shipSim)).IsGreater(0.999f);
    }
}
