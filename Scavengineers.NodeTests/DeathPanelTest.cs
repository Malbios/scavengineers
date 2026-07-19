using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.SaveLoad;
using PlayerScript = Scavengineers.Scripts.Player.Player;
using DeathPanelScript = Scavengineers.Scripts.Player.DeathPanel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the death screen (Player.Die()/ReloadAfterDeath()/
/// QuitAfterDeath()) — 0 Health now opens DeathPanel and waits for a Reload/Quit choice instead
/// of reloading immediately (see SaveManagerTest for the plain save/load round-trip this builds
/// on).</summary>
[TestSuite]
public class DeathPanelTest
{
    private static async Task KillAsync(SceneTree sceneTree, PlayerScript player)
    {
        var state = player.CapturePlayerState();
        state.HealthPercent = 0f;
        player.ApplyPlayerState(state);

        // _PhysicsProcess only checks HealthPercent on a real physics tick — let a couple run,
        // same pattern PlayerToolDurabilityTest already uses for its own RayCast3D resolution.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Death_OpensTheDeathPanel_InsteadOfReloadingImmediately()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        await KillAsync(sceneTree, player);

        AssertBool(player.GetNode<DeathPanelScript>("HUD/DeathPanel").Visible).IsTrue();
        AssertBool(player.IsAwaitingDeathChoice).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ReloadButton_ReloadsTheLastSave_AndClosesThePanel()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var tempPath = Path.GetTempFileName();
        try
        {
            var player = PlayerTestHarness.CreateAttached(sceneTree);
            var manager = AutoFree(new SaveManager { PlayerRef = player, SavePath = tempPath });
            sceneTree.Root.AddChild(manager);

            player.Position = new Vector3(3, 1, -2);
            player.AddCredits(500);
            var expectedCredits = player.Credits;
            manager.Save();

            await KillAsync(sceneTree, player);
            var deathPanel = player.GetNode<DeathPanelScript>("HUD/DeathPanel");
            AssertBool(deathPanel.Visible).IsTrue();

            // Exercises the real click-to-handler wiring, not just Player.ReloadAfterDeath()
            // directly — an unwired button (Container never assigned, Pressed never connected)
            // is exactly the class of bug the PDA's second cartridge slot had earlier.
            deathPanel.ReloadButton!.EmitSignal(Button.SignalName.Pressed);

            AssertBool(deathPanel.Visible).IsFalse();
            AssertBool(player.IsAwaitingDeathChoice).IsFalse();
            AssertBool(player.Position == new Vector3(3, 1, -2)).IsTrue();
            AssertBool(player.Credits == expectedCredits).IsTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task SaveManager_SkipsSaving_WhileTheDeathScreenIsPending()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var tempPath = Path.GetTempFileName();
        try
        {
            var player = PlayerTestHarness.CreateAttached(sceneTree);
            var manager = AutoFree(new SaveManager { PlayerRef = player, SavePath = tempPath });
            sceneTree.Root.AddChild(manager);

            manager.Save();

            await KillAsync(sceneTree, player);
            AssertBool(player.IsAwaitingDeathChoice).IsTrue();

            // Attempt to overwrite the last good save while the panel is still up.
            manager.Save();

            var savedData = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(tempPath));
            AssertFloat(savedData!.Player.HealthPercent).IsGreater(0f);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void QuitButton_IsWiredToAHandler()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        var deathPanel = player.GetNode<DeathPanelScript>("HUD/DeathPanel");

        // Deliberately never EmitSignal's this one — that would call the real GetTree().Quit()
        // and kill the test runner process. Wiring is checked, not invoked.
        AssertBool(deathPanel.QuitButton!.GetSignalConnectionList("pressed").Count > 0).IsTrue();
    }
}
