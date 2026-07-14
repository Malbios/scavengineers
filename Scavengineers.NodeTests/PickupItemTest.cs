using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for PickupItem/ContainerPickupItem's one-time startup freeze:
/// both start frozen (dodging the floor-collision-generation race on the very first frame — see
/// PickupItem's own doc comment) and must unfreeze on their own first physics tick regardless of
/// room/vacuum state, since gravity itself is handled separately by ShipAtmosphereZone's Area3D
/// override — a loose item should be a live, pushable physics body under normal gravity too, not
/// just while drifting in a breach.</summary>
[TestSuite]
public class PickupItemTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void PickupItem_UnfreezesAfterFirstPhysicsTick_RegardlessOfRoomVacuumState()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var item = AutoFree(new PickupItem());
        sceneTree.Root.AddChild(item); // triggers _Ready(): Freeze = true

        AssertBool(item.Freeze).IsTrue(); // sanity: starts frozen

        item._PhysicsProcess(0); // simulate the first physics tick

        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ContainerPickupItem_UnfreezesAfterFirstPhysicsTick_RegardlessOfRoomVacuumState()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var item = AutoFree(new ContainerPickupItem());
        sceneTree.Root.AddChild(item); // triggers _Ready(): Freeze = true

        AssertBool(item.Freeze).IsTrue(); // sanity: starts frozen

        item._PhysicsProcess(0); // simulate the first physics tick

        AssertBool(item.Freeze).IsFalse();
    }
}
