using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for LadderVerbTarget/Player's climbing state — real
/// Input.IsPhysicalKeyPressed-driven vertical movement isn't fakeable in this project's NodeTests
/// (no established way to simulate held OS keys, see DockingMinigamePanelTest's own doc comment),
/// so this covers what IS observable without it: entering climbing state, and auto-release by
/// position (which fires unconditionally, not gated on any simulated key).</summary>
[TestSuite]
public class LadderVerbTargetTest
{
    private static LadderVerbTarget MakeLadder(SceneTree sceneTree, Vector3 bottom, Vector3 top)
    {
        var ladder = AutoFree(new LadderVerbTarget());
        sceneTree.Root.AddChild(ladder);

        // Ladder itself sits at world origin (its default Position), so each anchor's own local
        // Position doubles as its GlobalPosition here.
        var bottomAnchor = new Node3D { Position = bottom };
        ladder.AddChild(bottomAnchor);
        var topAnchor = new Node3D { Position = top };
        ladder.AddChild(topAnchor);

        ladder.BottomAnchor = bottomAnchor;
        ladder.TopAnchor = topAnchor;

        return ladder;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ExecuteVerb_ClimbLadder_PutsThePlayerIntoClimbingState()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        var ladder = MakeLadder(sceneTree, Vector3.Zero, new Vector3(0, 2.05f, 0));

        AssertBool(player.IsClimbing).IsFalse();

        ladder.ExecuteVerb(ladder.AvailableVerbs[0], player.Inventory);

        AssertBool(player.IsClimbing).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Climbing_AutoReleases_OnceThePlayerPassesTheTopAnchorByTheReleaseMargin()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        var bottom = Vector3.Zero;
        var top = new Vector3(0, 2.05f, 0);
        var ladder = MakeLadder(sceneTree, bottom, top);

        ladder.ExecuteVerb(ladder.AvailableVerbs[0], player.Inventory);
        AssertBool(player.IsClimbing).IsTrue();

        // Simulate having already climbed most of the way — well past the top anchor plus its
        // own release margin — so the very next physics tick's auto-release fires without
        // needing any simulated W/S key input at all.
        player.GlobalPosition = top + new Vector3(0, 1f, 0);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(player.IsClimbing).IsFalse();
    }
}
