using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for IVerbTarget.HighlightVisual's default implementation (used by
/// the PDA scan-mode outline shader) — every real target is a Node, and the default just finds
/// its own first VisualInstance3D child, covering the overwhelming majority of targets (a mesh
/// spawned/wired as a direct child) with zero per-file changes. Uses AirlockDoorVerbTarget as a
/// representative, ordinary IVerbTarget — nothing about the default is airlock-specific.</summary>
[TestSuite]
public class HighlightVisualDefaultTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void HighlightVisual_FindsItsOwnFirstMeshInstanceChild()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB });
        var mesh = new MeshInstance3D { Name = "SlabMesh" };
        airlock.AddChild(mesh);
        airlock.SlabMesh = mesh;
        sceneTree.Root.AddChild(airlock);

        IVerbTarget target = airlock;
        AssertBool(ReferenceEquals(target.HighlightVisual, mesh)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void HighlightVisual_IsNull_WhenTheTargetHasNoVisualInstanceChildAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB });
        sceneTree.Root.AddChild(airlock);

        IVerbTarget target = airlock;
        AssertObject(target.HighlightVisual).IsNull();
    }
}
