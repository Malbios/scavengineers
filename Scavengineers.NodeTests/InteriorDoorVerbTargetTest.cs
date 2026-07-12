using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the Derelict's interior door: it has no working power grid
/// (see ShipSim.HasPowerGrid, never set for it), so before InteriorDoorVerbTarget.RequiresPower
/// existed, AvailableVerbs was always empty for it — the door could never be opened or closed by
/// the player, though it still defaulted to open and passable. Also verifies the actual physical
/// passability claim end-to-end (SlabCollision.Disabled) rather than trusting it by inspection.</summary>
[TestSuite]
public class InteriorDoorVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_WithoutRequiresPowerFalse_HasNoAvailableVerbs()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(door);

        AssertBool(door.RequiresPower).IsTrue(); // default, matches the Home Ship's gated door
        AssertThat(door.AvailableVerbs).IsEmpty(); // the bug: never powered, never interactable
    }

    [TestCase]
    [RequireGodotRuntime]
    public void UnpoweredShip_WithRequiresPowerFalse_OffersCloseVerbWhileOpen()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim, RequiresPower = false });
        sceneTree.Root.AddChild(door);

        AssertThat(door.AvailableVerbs).HasSize(1);
        AssertBool(door.IsOpen).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_Open_DisablesSlabCollision_SoTheDoorwayIsActuallyPassable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasFireHazard = true, HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        var door = AutoFree(new InteriorDoorVerbTarget { ShipSimRef = shipSim, RequiresPower = false });
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
