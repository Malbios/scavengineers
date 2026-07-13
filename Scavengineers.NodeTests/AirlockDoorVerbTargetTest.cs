using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage mirroring InteriorDoorVerbTargetTest: an unpowered airlock offers
/// only a crowbar-pry verb regardless of open/closed state, and the same verb forces it open when
/// closed or shut again when already open — no motor to drive the mechanism either way.</summary>
[TestSuite]
public class AirlockDoorVerbTargetTest
{
    private static AirlockDoorVerbTarget CreateUnpoweredAirlock(SceneTree sceneTree)
    {
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB });
        sceneTree.Root.AddChild(airlock);
        return airlock;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_ClosedAirlock_OffersOnlyPryVerb()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var airlock = CreateUnpoweredAirlock(sceneTree);

        AssertBool(airlock.IsOpen).IsFalse();
        AssertThat(airlock.AvailableVerbs).HasSize(1);
        AssertThat(airlock.AvailableVerbs[0].Id).IsEqual("pry_airlock");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_OpenAirlock_OffersPryVerbToForceItShut()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var airlock = CreateUnpoweredAirlock(sceneTree);
        airlock.ApplySaveState(true); // simulate an already-open airlock, e.g. loaded from a save

        AssertThat(airlock.AvailableVerbs).HasSize(1);
        AssertThat(airlock.AvailableVerbs[0].Id).IsEqual("pry_airlock");
    }
}
