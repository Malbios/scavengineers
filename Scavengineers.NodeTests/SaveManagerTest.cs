using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for SaveManager's own Save/Load node logic — previously only the
/// SaveData DTOs themselves were round-tripped through JSON (see
/// Scavengineers.Scripts.Tests/SaveLoad/SaveDataSerializationTests.cs), never the manager's own
/// file-handling branches (missing file, corrupt JSON, the dropped-container legacy-Contents-dict
/// fallback). SavePath is overridden to a per-test temp file so none of this ever touches the
/// player's real save data.</summary>
[TestSuite]
public class SaveManagerTest
{
    private static (PlayerScript Player, SaveManager Manager) MakeHarness(string savePath)
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var manager = AutoFree(new SaveManager { PlayerRef = player, SavePath = savePath });
        sceneTree.Root.AddChild(manager);

        return (player, manager);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_ReturnsFalse_WhenNoFileExists()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"scavengineers-test-{Guid.NewGuid()}.json");
        var (_, manager) = MakeHarness(missingPath);

        AssertBool(manager.Load()).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_ReturnsFalse_OnCorruptJson()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "{ not valid json ");
            var (_, manager) = MakeHarness(tempPath);

            AssertBool(manager.Load()).IsFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SaveThenLoad_RoundTripsPlayerPositionAndCredits()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var (player, manager) = MakeHarness(tempPath);
            player.Position = new Vector3(3, 1, -2);
            player.AddCredits(500);
            var expectedCredits = player.Credits;

            manager.Save();

            player.Position = Vector3.Zero;

            AssertBool(manager.Load()).IsTrue();
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
    public void SaveThenLoad_DroppedContainerWithLegacyContentsDict_StillRestoresItems()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var (_, manager) = MakeHarness(tempPath);
            var sceneTree = (SceneTree)Engine.GetMainLoop();

            var data = new SaveData
            {
                DroppedContainers = new List<DroppedContainerSaveData>
                {
                    new()
                    {
                        ItemId = "backpack",
                        PosX = 1,
                        PosY = 1,
                        PosZ = 1,
                        Contents = new Dictionary<string, int> { ["scrap_metal"] = 3 },
                        // Slots deliberately left empty — the exact shape of a save predating
                        // per-slot state, forcing Load's legacy-dict-replay branch.
                    },
                },
            };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(data));

            AssertBool(manager.Load()).IsTrue();

            var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "backpack");
            AssertBool(dropped is not null).IsTrue();
            AssertBool(dropped!.Contents!.CountOf("scrap_metal") == 3).IsTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ProcedurallyGeneratedShip_RollsFreshOnFirstBoot_ThenReadsTheSameSeedBackAfterSaving()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var sceneTree = (SceneTree)Engine.GetMainLoop();
            var (player, manager) = MakeHarness(tempPath);

            var firstBoot = AutoFree(new ShipSim
            {
                ProcedurallyGenerate = true,
                SaveId = "test_procedural_ship",
                SavePathOverride = tempPath,
            });
            firstBoot.AddToGroup("saveable");
            sceneTree.Root.AddChild(firstBoot);

            AssertBool(firstBoot.LayoutSeed.HasValue).IsTrue();
            var rolledSeed = firstBoot.LayoutSeed!.Value;
            var rolledGridWidth = firstBoot.GridWidth;

            manager.Save();

            var secondBoot = AutoFree(new ShipSim
            {
                ProcedurallyGenerate = true,
                SaveId = "test_procedural_ship",
                SavePathOverride = tempPath,
            });
            sceneTree.Root.AddChild(secondBoot);

            AssertBool(secondBoot.LayoutSeed == rolledSeed).IsTrue();
            AssertBool(secondBoot.GridWidth == rolledGridWidth).IsTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Autosave_FiresAfterTheConfiguredInterval_WritingTheSaveFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"scavengineers-test-{Guid.NewGuid()}.json");
        try
        {
            var sceneTree = (SceneTree)Engine.GetMainLoop();
            var player = PlayerTestHarness.CreateAttached(sceneTree);

            // AutosaveIntervalSeconds must be set before AddChild — the Timer it drives is built
            // in _Ready(), same timing constraint SavePath already has.
            var manager = AutoFree(new SaveManager { PlayerRef = player, SavePath = tempPath, AutosaveIntervalSeconds = 0.1f });
            sceneTree.Root.AddChild(manager);

            AssertBool(File.Exists(tempPath)).IsFalse();

            await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

            AssertBool(File.Exists(tempPath)).IsTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Save_ShowsThePlayersSavedFlash()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var (player, manager) = MakeHarness(tempPath);

            manager.Save();

            AssertBool(player.GetNode<Label>("HUD/SavedLabel").Visible).IsTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task SavedFlash_HidesAgain_AfterItsOwnWindowElapses()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var tempPath = Path.GetTempFileName();
        try
        {
            var (player, manager) = MakeHarness(tempPath);

            manager.Save();
            var savedLabel = player.GetNode<Label>("HUD/SavedLabel");
            AssertBool(savedLabel.Visible).IsTrue();

            // Past the 2s flash window with real margin.
            await sceneTree.ToSignal(sceneTree.CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);

            AssertBool(savedLabel.Visible).IsFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
