using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for tool durability: crowbar/power_drill/wrench now wear down
/// with actual use (Player.Interact), reusing the held item's own hand-slot Charge to mean
/// durability, and a worn-out (0 Charge) tool's verb becomes unaffordable (Player.IsAffordable).
/// Drives the real Interact() path via a right-click InputEventMouseButton against a real
/// AirlockDoorVerbTarget collider — its unpowered PryVerb (crowbar, Consumed: false) is the
/// simplest existing verb gated on a durable tool.</summary>
[TestSuite]
public class PlayerToolDurabilityTest
{
    private static (Scavengineers.Scripts.Player.Player Player, AirlockDoorVerbTarget Airlock) MakeHarness(SceneTree sceneTree, float crowbarCharge)
    {
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("crowbar", 1, crowbarCharge));

        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        // Directly in front of the player's default (identity-transform) camera, whose forward
        // is -Z — matches this project's own established "player at world origin" test harness
        // convention (see PlayerSaveStateTest's own wall placements).
        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB, Position = new Vector3(0, 0, -2) });
        airlock.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(airlock);

        var interactRay = player.GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        interactRay.TargetPosition = new Vector3(0, 0, -10);

        return (player, airlock);
    }

    private static async Task RightClickAsync(SceneTree sceneTree, Scavengineers.Scripts.Player.Player player)
    {
        // RayCast3D only resolves its collision during the physics step — let a couple of ticks
        // run before the simulated click reads IsColliding().
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        player._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Interact_UsingACrowbarViaPryVerb_WearsDownItsOwnDurability()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, airlock) = MakeHarness(sceneTree, crowbarCharge: 1f);

        await RightClickAsync(sceneTree, player);

        // PryVerb takes 1.5s to complete (see AirlockDoorVerbTarget's own OnCycleComplete), so
        // IsOpen doesn't flip yet — CurrentVerbProgress going non-null is what proves ExecuteVerb
        // actually ran, immediately, without waiting out the real-time cycle.
        AssertBool(airlock.CurrentVerbProgress is not null).IsTrue();
        var slot = player.Inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex];
        AssertBool(slot is { ItemId: "crowbar" }).IsTrue();
        AssertFloat(slot!.Value.Charge).IsEqual(0.98f); // 1f - ToolWearPerUse (0.02f)
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Interact_WithAZeroDurabilityCrowbar_NeverExecutesTheVerbAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, airlock) = MakeHarness(sceneTree, crowbarCharge: 0f); // worn out

        await RightClickAsync(sceneTree, player);

        AssertBool(airlock.CurrentVerbProgress is null).IsTrue();
        AssertBool(airlock.IsOpen).IsFalse();
    }
}
