using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Travel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the docking minigame's own simulation/tolerance logic —
/// drives DockingMinigamePanel.Tick directly with synthetic input rather than
/// Input.IsPhysicalKeyPressed (real, continuously-held OS key state that NodeTests has no
/// established way to fake — see Tick's own doc comment), and ResetAttempt's explicit-override
/// parameters to set up deterministic starting states instead of its normal randomized one.</summary>
[TestSuite]
public class DockingMinigamePanelTest
{
    private const string AbortedMessage = "HUD_DOCKING_ABORTED"; // Tr() echoes the raw key back
                                                                   // unchanged in this isolated
                                                                   // catalog-less test project.

    private static DockingMinigamePanel MakePanel(SceneTree sceneTree)
    {
        var panel = new DockingMinigamePanel { Name = "DockingPanel", Visible = true };
        panel.View = new DockingView();
        panel.StatusLabel = new Label();
        panel.DockButton = new Button();
        panel.AddChild(panel.View);
        panel.AddChild(panel.StatusLabel);
        panel.AddChild(panel.DockButton);

        var freed = AutoFree(panel)!;
        sceneTree.Root.AddChild(freed);
        return freed;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Tick_WithVelocityExceedingMaxSafeSpeed_Aborts()
    {
        var panel = MakePanel((SceneTree)Engine.GetMainLoop());

        // Centered (no misalignment risk) but already flying far too fast.
        panel.ResetAttempt(startingOffset: Vector2.Zero, startingVelocity: new Vector2(40, 0), startingDistance: 50f);

        panel.Tick(0.01f, Vector2.Zero, false);

        AssertBool(panel.StatusLabel!.Text == AbortedMessage).IsTrue();
        AssertBool(panel.DockButton!.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Tick_WithOffsetExceedingMaxMisalignment_Aborts()
    {
        var panel = MakePanel((SceneTree)Engine.GetMainLoop());

        // Stationary (no speed risk) but already drifted far too far off-axis.
        panel.ResetAttempt(startingOffset: new Vector2(70, 0), startingVelocity: Vector2.Zero, startingDistance: 50f);

        panel.Tick(0.01f, Vector2.Zero, false);

        AssertBool(panel.StatusLabel!.Text == AbortedMessage).IsTrue();
        AssertBool(panel.DockButton!.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task AfterAnAbort_RetriesAutomatically_WithoutImmediatelyRe_Aborting()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var panel = MakePanel(sceneTree);

        panel.ResetAttempt(startingOffset: Vector2.Zero, startingVelocity: new Vector2(40, 0), startingDistance: 50f);
        panel.Tick(0.01f, Vector2.Zero, false);
        AssertBool(panel.StatusLabel!.Text == AbortedMessage).IsTrue();

        // Past AbortMessageSeconds (1.5s) with real margin — the abort-reset Timer fires
        // ResetAttempt() with its normal randomized (safe, well within both thresholds) values.
        await sceneTree.ToSignal(sceneTree.CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

        panel.Tick(0.01f, Vector2.Zero, false);

        AssertBool(panel.StatusLabel!.Text == AbortedMessage).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DockButton_DisabledFarFromTarget_EnabledOnceWithinAllThreeTolerances()
    {
        var panel = MakePanel((SceneTree)Engine.GetMainLoop());

        panel.ResetAttempt(startingOffset: new Vector2(50, 0), startingVelocity: Vector2.Zero, startingDistance: 80f);
        AssertBool(panel.DockButton!.Disabled).IsTrue();

        // Well within DockAlignmentTolerance(8)/DockDistanceTolerance(10)/DockMaxSafeSpeed(5) all
        // at once.
        panel.ResetAttempt(startingOffset: new Vector2(2, 0), startingVelocity: Vector2.Zero, startingDistance: 5f);
        AssertBool(panel.DockButton!.Disabled).IsFalse();
    }
}
