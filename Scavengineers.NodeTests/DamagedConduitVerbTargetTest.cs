using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for DamagedConduitVerbTarget's own world position: its hand-
/// authored Transform never used to read ShipSim.DamagedConduitCell at all, which was harmless
/// only because every ship's conduit cell was the same hardcoded value — already provably wrong
/// once layouts (catalog or procedurally generated) can put that cell somewhere else.</summary>
[TestSuite]
public class DamagedConduitVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task RepositionsItselfToMatchShipSimsDamagedConduitCell()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { HasFireHazard = true };
        shipSim.ApplyLayout(new ShipLayoutCatalog.ShipLayoutDefinition
        {
            GridWidth = 18,
            RoomSplitColumns = [6, 12],
            HasFireHazard = true,
            DamagedConduitCell = new ShipLayoutCatalog.CellPosition { X = 9, Y = 3 },
            FireGeneratorCell = new ShipLayoutCatalog.CellPosition { X = 9, Y = 4 },
        });
        shipRoot.AddChild(shipSim);

        var conduit = new DamagedConduitVerbTarget { ShipSimRef = shipSim, Position = new Vector3(-1.5f, 0.3f, 0.5f) };
        shipRoot.AddChild(conduit);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        // Tile (9,3) -> local x = 9-3+0.5 = 6.5, z = 3-3+0.5 = 0.5. Original hand-authored Y
        // (0.3, the mount height) must be preserved, only X/Z move.
        AssertFloat(conduit.Position.X).IsEqual(6.5f);
        AssertFloat(conduit.Position.Z).IsEqual(0.5f);
        AssertFloat(conduit.Position.Y).IsEqual(0.3f);

        shipRoot.QueueFree();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task HidesAndDisablesItself_WhenTheShipHasNoFireHazardAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { HasFireHazard = false };
        shipRoot.AddChild(shipSim);

        var collider = new CollisionShape3D();
        var conduit = new DamagedConduitVerbTarget { ShipSimRef = shipSim, Collider = collider };
        conduit.AddChild(collider);
        shipRoot.AddChild(conduit);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(conduit.Visible).IsFalse();
        AssertBool(collider.Disabled).IsTrue();

        shipRoot.QueueFree();
    }
}
