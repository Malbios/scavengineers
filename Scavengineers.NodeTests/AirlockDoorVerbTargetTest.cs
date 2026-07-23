using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;

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

    [TestCase]
    [RequireGodotRuntime]
    public void RebindFarSide_ToDifferentShip_ClosesDoorAndRetargetsBridge()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var airlock = CreateUnpoweredAirlock(sceneTree);
        airlock.ApplySaveState(true); // open, docked at the original ShipBRef

        var shipC = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipC);

        airlock.RebindFarSide(shipC);

        AssertBool(airlock.IsOpen).IsFalse();
        AssertBool(ReferenceEquals(airlock.ShipBRef, shipC)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RebindFarSide_ToSameShip_IsNoOp()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var airlock = CreateUnpoweredAirlock(sceneTree);
        airlock.ApplySaveState(true); // open

        airlock.RebindFarSide(airlock.ShipBRef!);

        AssertBool(airlock.IsOpen).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RebindFarSide_AlsoRebindsThePartnerDoor()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var airlock = CreateUnpoweredAirlock(sceneTree);
        var originalPartner = AutoFree(new AirlockDoorVerbTarget());
        sceneTree.Root.AddChild(originalPartner);
        airlock.PartnerDoorRef = originalPartner;

        var shipC = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipC);
        var newPartner = AutoFree(new AirlockDoorVerbTarget());
        sceneTree.Root.AddChild(newPartner);

        airlock.RebindFarSide(shipC, newPartner);

        AssertBool(ReferenceEquals(airlock.PartnerDoorRef, newPartner)).IsTrue();
        // Bidirectional: the new partner points back at this door, and the old one no longer
        // does — see RebindFarSide's own doc comment for why both directions matter.
        AssertBool(ReferenceEquals(newPartner.PartnerDoorRef, airlock)).IsTrue();
        AssertObject(originalPartner.PartnerDoorRef).IsNull();
    }

    /// <summary>Regression coverage for a shared, rebindable airlock (StationAirlock/
    /// DerelictAirlock) whose far side was never bound because it doesn't match the player's
    /// current destination type — e.g. DerelictAirlock while docked at a Station. ShipBRef stays
    /// permanently null in that case, and used to make AvailableVerbs return [] entirely (not even
    /// Pry). A door with no bound far side represents empty space on the other side, and must stay
    /// fully operable, breaching only its own ship's side when opened.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public void AirlockWithNoBoundFarSide_StillOffersVerbs_AndBreachesOnlyItsOwnSideWhenOpened()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, TileA = new Vector2I(13, 2) });
        sceneTree.Root.AddChild(airlock);
        airlock.Docked = false;

        AssertThat(airlock.AvailableVerbs).IsNotEmpty();

        airlock.ApplySaveState(true);

        AssertBool(shipA.Deck.IsHullBreached(new CellCoord(13, 2))).IsTrue();
    }
}
