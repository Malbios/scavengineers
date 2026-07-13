using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the interior door's unpowered behavior: a ship with no
/// working power grid (e.g. the Derelict) starts its interior doors closed rather than the old
/// hardcoded "always starts open", and offers only a crowbar-pry verb to get through either
/// direction — no motor to drive the mechanism, so the same pry verb forces it open when closed
/// or shut again when already open. Also verifies the actual physical passability claim
/// end-to-end (SlabCollision.Disabled) rather than trusting it by inspection.</summary>
[TestSuite]
public class InteriorDoorVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_ClosedDoor_OffersOnlyPryVerb()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(door);

        AssertBool(door.IsOpen).IsFalse(); // the field default, before the deferred power-derived apply even runs
        AssertThat(door.AvailableVerbs).HasSize(1);
        AssertThat(door.AvailableVerbs[0].Id).IsEqual("pry_door");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_OpenDoor_OffersPryVerbToForceItShut()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(door);
        door.ApplySaveState(true); // simulate an already-open door, e.g. loaded from an old save

        AssertThat(door.AvailableVerbs).HasSize(1);
        AssertThat(door.AvailableVerbs[0].Id).IsEqual("pry_door");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_Open_DisablesSlabCollision_SoTheDoorwayIsActuallyPassable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim });
        var slabMesh = new MeshInstance3D { Name = "SlabMesh" };
        var slabCollision = new CollisionShape3D { Name = "SlabCollision", Shape = new BoxShape3D() };
        door.AddChild(slabMesh);
        door.AddChild(slabCollision);
        door.SlabMesh = slabMesh;
        door.SlabCollision = slabCollision;
        sceneTree.Root.AddChild(door);

        // Closed first, to prove the assertion below isn't just the untouched scene default.
        door.ApplySaveState(false);
        AssertBool(slabCollision.Disabled).IsFalse();
        AssertBool(slabMesh.Visible).IsTrue();

        door.ApplySaveState(true);
        AssertBool(slabCollision.Disabled).IsTrue();
        AssertBool(slabMesh.Visible).IsFalse();
    }
}
