using System.Collections.Generic;
using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Player;
using Scavengineers.Scripts.SaveLoad;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for two of Player.cs's riskiest, previously-untested branches:
/// ApplyPlayerState's legacy-save fallback (the exact path a save from before per-slot inventory
/// state runs through) and TryDropInWorld's battery-eject-to-world routing (dragging the drill/
/// flashlight's battery slot straight into the world). Uses PlayerTestHarness since this project
/// has no res://Scenes/Player.tscn to load from.</summary>
[TestSuite]
public class PlayerSaveStateTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_WithLegacyInventoryDict_ReplaysItIntoHandSlots()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            // HandSlots/BackpackSlots deliberately left at their default empty lists — the exact
            // shape of a save predating per-slot state. Two distinct items (not one stacked
            // count) — this project's isolated ItemCatalog can't load the real Data/items.json
            // (see PlayerTestHarness), so every item falls back to a stack size of 1; this keeps
            // the test independent of that unrelated fallback behavior.
            Inventory = new Dictionary<string, int> { ["scrap_metal"] = 1, ["spare_parts"] = 1 },
        };

        player.ApplyPlayerState(data);

        AssertBool(player.Inventory.CountOf("scrap_metal") == 1).IsTrue();
        AssertBool(player.Inventory.CountOf("spare_parts") == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_WithHandSlots_RestoresPositionally_IgnoringTheLegacyDict()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            // Both populated, disagreeing — the new-format list must win.
            HandSlots = new List<SlotSaveData?>
            {
                new() { ItemId = "battery", Count = 1, Charge = 0.55f },
                null,
            },
            Inventory = new Dictionary<string, int> { ["scrap_metal"] = 99 },
        };

        player.ApplyPlayerState(data);

        AssertBool(player.Inventory.CountOf("scrap_metal") == 0).IsTrue(); // legacy dict ignored
        var restored = player.Inventory.Slots[PlayerInventory.LeftHandSlotIndex];
        AssertBool(restored is { ItemId: "battery", Count: 1, Charge: 0.55f }).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_DrillBatterySlot_SpawnsALooseBatteryWithRealChargeAndClearsTheDrill()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.37f);

        // A solid target within reach, straight ahead of the camera's default forward (-Z), so
        // Player.ResolveWorldDropPosition's raycast actually has something to hit.
        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var source = AutoFree(new InventorySlotUI { SpecializedSlotKey = "drill_battery" });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        AssertBool(player.Inventory.Drill!.HasItem).IsFalse();
        var dropped = sceneTree.Root.GetChildren().OfType<PickupItem>().FirstOrDefault(p => p.ItemId == "battery");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(Mathf.IsEqualApprox(dropped!.Charge, 0.37f)).IsTrue();

        AutoFree(dropped);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_FlashlightBatterySlot_SpawnsALooseBatteryWithRealChargeAndClearsTheFlashlight()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 0.63f);

        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var source = AutoFree(new InventorySlotUI { SpecializedSlotKey = "flashlight_battery" });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        AssertBool(player.Inventory.Flashlight!.HasItem).IsFalse();
        var dropped = sceneTree.Root.GetChildren().OfType<PickupItem>().FirstOrDefault(p => p.ItemId == "battery");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(Mathf.IsEqualApprox(dropped!.Charge, 0.63f)).IsTrue();

        AutoFree(dropped);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyThenCapturePlayerState_RoundTripsAFullySuitedPlayer_TanksHelmetAndCo2Included()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            TorsoItemId = "eva_torso_suit",
            TorsoSlots = new List<SlotSaveData?> { new() { ItemId = "scrap_metal", Count = 1, Charge = 1f }, null },
            TorsoSlotCount = 2,
            HasSuitO2Tank = true,
            SuitO2Charge = 0.75f,
            HasSuitN2Tank = true,
            SuitN2Charge = 0.5f,
            HasSuitFilter = true,
            SuitFilterCharge = 0.9f,
            HasSuitBattery = true,
            SuitBatteryCharge = 0.6f,
            HeadItemId = "eva_helmet",
            CO2Percent = 42f,
        };

        player.ApplyPlayerState(data);
        var roundTripped = player.CapturePlayerState();

        AssertBool(roundTripped.TorsoItemId == "eva_torso_suit").IsTrue();
        AssertBool(roundTripped.TorsoSlots.Any(s => s?.ItemId == "scrap_metal")).IsTrue();
        AssertBool(roundTripped.HeadItemId == "eva_helmet").IsTrue();
        AssertBool(roundTripped.HasSuitO2Tank).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.SuitO2Charge, 0.75f)).IsTrue();
        AssertBool(roundTripped.HasSuitN2Tank).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.SuitN2Charge, 0.5f)).IsTrue();
        AssertBool(roundTripped.HasSuitFilter).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.SuitFilterCharge, 0.9f)).IsTrue();
        AssertBool(roundTripped.HasSuitBattery).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.SuitBatteryCharge, 0.6f)).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.CO2Percent, 42f)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_WithEveryFieldAtItsJsonMissingDefault_LoadsAsNoSuitAndNoCo2Buildup()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        player.ApplyPlayerState(new PlayerSaveData()); // every suit-related field at its default

        AssertBool(player.Inventory.Torso is null).IsTrue();
        AssertBool(player.Inventory.Head is null).IsTrue();
        AssertBool(player.Inventory.SuitO2 is null).IsTrue();
        AssertBool(player.Inventory.SuitN2 is null).IsTrue();
        AssertBool(player.Inventory.SuitFilter is null).IsTrue();
        AssertBool(player.Inventory.SuitBattery is null).IsTrue();

        var captured = player.CapturePlayerState();
        AssertBool(captured.CO2Percent == 0f).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_ARestoredEvaSuitHeldNotWorn_RestoresItsPocketsAndTanksAnyway()
    {
        // The exact gap Stage 6 (persistent-contents save schema) closes: a save capturing a suit
        // that's merely held (not equipped into "torso") must still restore its pocket contents
        // and tank state, not just silently lose them because TorsoItemId is null.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            TorsoItemId = null, // not worn
            HasEvaSuit = true, // but owned
            TorsoSlots = new List<SlotSaveData?> { new() { ItemId = "scrap_metal", Count = 1, Charge = 1f }, null },
            HasSuitO2Tank = true,
            SuitO2Charge = 0.75f,
            HasSuitN2Tank = true,
            SuitN2Charge = 0.5f,
        };

        player.ApplyPlayerState(data);

        AssertBool(player.Inventory.Torso is null).IsTrue();
        var pocketContents = player.Inventory.GetPersistentContents("eva_torso_suit");
        AssertBool(pocketContents is not null).IsTrue();
        AssertBool(pocketContents!.CountOf("scrap_metal") == 1).IsTrue();
        AssertBool(player.Inventory.SuitO2 is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitO2!.Charge, 0.75f)).IsTrue();
        AssertBool(player.Inventory.SuitN2 is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitN2!.Charge, 0.5f)).IsTrue();

        var roundTripped = player.CapturePlayerState();
        AssertBool(roundTripped.TorsoItemId is null).IsTrue();
        AssertBool(roundTripped.HasEvaSuit).IsTrue();
        AssertBool(roundTripped.TorsoSlots.Any(s => s?.ItemId == "scrap_metal")).IsTrue();
        AssertBool(roundTripped.HasSuitO2Tank).IsTrue();
        AssertBool(Mathf.IsEqualApprox(roundTripped.SuitO2Charge, 0.75f)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_ARestoredBackpackHeldNotWorn_RestoresItsContentsAnyway()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            BackpackItemId = null, // not worn
            OwnedBackpackItemId = "backpack", // but owned
            BackpackSlots = new List<SlotSaveData?> { new() { ItemId = "scrap_metal", Count = 1, Charge = 1f }, null },
            BackpackSlotCount = 2,
        };

        player.ApplyPlayerState(data);

        AssertBool(player.Inventory.Backpack is null).IsTrue();
        var contents = player.Inventory.GetPersistentContents("backpack");
        AssertBool(contents is not null).IsTrue();
        AssertBool(contents!.CountOf("scrap_metal") == 1).IsTrue();

        var roundTripped = player.CapturePlayerState();
        AssertBool(roundTripped.BackpackItemId is null).IsTrue();
        AssertBool(roundTripped.OwnedBackpackItemId == "backpack").IsTrue();
        AssertBool(roundTripped.BackpackSlots.Any(s => s?.ItemId == "scrap_metal")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyThenCapturePlayerState_RoundTripsAWornPdaAndItsCartridge()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            PdaItemId = "pda",
            HasPda = true,
            PdaSlots = new List<SlotSaveData?> { new() { ItemId = "health_scan_cartridge", Count = 1, Charge = 1f } },
            PdaSlotCount = 1,
        };

        player.ApplyPlayerState(data);
        var roundTripped = player.CapturePlayerState();

        AssertBool(roundTripped.PdaItemId == "pda").IsTrue();
        AssertBool(roundTripped.HasPda).IsTrue();
        AssertBool(roundTripped.PdaSlots.Any(s => s?.ItemId == "health_scan_cartridge")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyPlayerState_ARestoredPdaHeldNotWorn_RestoresItsCartridgeAnyway()
    {
        // Same gap as the EVA suit/backpack cases above — a PDA that's merely held (not equipped
        // into "pda") must still restore its cartridge pocket, not silently lose it because
        // PdaItemId is null.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var data = new PlayerSaveData
        {
            PdaItemId = null, // not worn
            HasPda = true, // but owned
            PdaSlots = new List<SlotSaveData?> { new() { ItemId = "health_scan_cartridge", Count = 1, Charge = 1f } },
            PdaSlotCount = 1,
        };

        player.ApplyPlayerState(data);

        AssertBool(player.Inventory.GetEquippedContainer("pda") is null).IsTrue();
        var pocketContents = player.Inventory.GetPersistentContents("pda");
        AssertBool(pocketContents is not null).IsTrue();
        AssertBool(pocketContents!.CountOf("health_scan_cartridge") == 1).IsTrue();

        var roundTripped = player.CapturePlayerState();
        AssertBool(roundTripped.PdaItemId is null).IsTrue();
        AssertBool(roundTripped.HasPda).IsTrue();
        AssertBool(roundTripped.PdaSlots.Any(s => s?.ItemId == "health_scan_cartridge")).IsTrue();
    }
}
