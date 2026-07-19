using System.Threading.Tasks;

using GdUnit4;
using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the low-health warning overlay — every other player stat
/// (O2, freezing, burning, smoke) already gets a full-screen HUD cue; Health previously had none
/// until it hit 0 and the death screen took over. Reuses the same ApplyPlayerState-to-set-Health
/// trick DeathPanelTest's own KillAsync uses, just above 0 so the death screen doesn't also
/// trigger.</summary>
[TestSuite]
public class PlayerLowHealthOverlayTest
{
    private static async Task SetHealthAsync(SceneTree sceneTree, PlayerScript player, float healthPercent)
    {
        var state = player.CapturePlayerState();
        state.HealthPercent = healthPercent;
        player.ApplyPlayerState(state);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Health_AtOrBelowTheThreshold_ShowsTheOverlay()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        await SetHealthAsync(sceneTree, player, 25f);

        AssertBool(player.GetNode<ColorRect>("HUD/LowHealthOverlay").Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Health_AboveTheThreshold_HidesTheOverlay()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        // Above the threshold first, so this proves the overlay isn't just always-on.
        await SetHealthAsync(sceneTree, player, 100f);
        AssertBool(player.GetNode<ColorRect>("HUD/LowHealthOverlay").Visible).IsFalse();

        // Dip below the threshold, then back above it — proves the overlay actually reacts to
        // Health changing rather than latching once triggered.
        await SetHealthAsync(sceneTree, player, 10f);
        AssertBool(player.GetNode<ColorRect>("HUD/LowHealthOverlay").Visible).IsTrue();

        await SetHealthAsync(sceneTree, player, 30f);
        AssertBool(player.GetNode<ColorRect>("HUD/LowHealthOverlay").Visible).IsFalse();
    }
}
