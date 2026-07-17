using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for IVerbTarget.HighlightVisual's default implementation (used by
/// the PDA scan-mode outline shader) — every real target is a Node, and the default finds ALL of
/// its own direct VisualInstance3D children (not just the first — a target like the real airlock
/// has a separate frame + slab mesh, both meant to be highlighted together), covering the
/// overwhelming majority of targets with zero per-file changes. Uses AirlockDoorVerbTarget as a
/// representative, ordinary IVerbTarget — nothing about the default is airlock-specific (the real
/// airlock no longer overrides it at all, see AirlockDoorVerbTarget.cs).</summary>
[TestSuite]
public class HighlightVisualDefaultTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void HighlightVisual_FindsAllOfItsOwnMeshInstanceChildren()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB });
        var frameMesh = new MeshInstance3D { Name = "FrameMesh" };
        var slabMesh = new MeshInstance3D { Name = "SlabMesh" };
        airlock.AddChild(frameMesh);
        airlock.AddChild(slabMesh);
        airlock.SlabMesh = slabMesh;
        sceneTree.Root.AddChild(airlock);

        IVerbTarget target = airlock;
        AssertBool(target.HighlightVisual.Contains(frameMesh)).IsTrue();
        AssertBool(target.HighlightVisual.Contains(slabMesh)).IsTrue();
        AssertInt(target.HighlightVisual.Count).IsEqual(2);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void HighlightVisual_IsEmpty_WhenTheTargetHasNoVisualInstanceChildAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var airlock = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB });
        sceneTree.Root.AddChild(airlock);

        IVerbTarget target = airlock;
        AssertInt(target.HighlightVisual.Count).IsEqual(0);
    }
}
