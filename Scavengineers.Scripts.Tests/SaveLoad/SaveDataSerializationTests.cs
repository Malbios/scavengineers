using System.Text.Json;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;

namespace Scavengineers.Scripts.Tests.SaveLoad;

/// <summary>Round-trips SaveData/PlayerSaveData/BuildTargetSaveData through the exact same
/// System.Text.Json calls SaveManager.Save/Load use, to catch schema drift (a renamed/removed
/// property silently dropping data) without needing a real save file or a running game.</summary>
public class SaveDataSerializationTests
{
    [Fact]
    public void SaveData_RoundTrips_TopLevelFields()
    {
        var data = new SaveData
        {
            Version = 1,
            Player = new PlayerSaveData
            {
                PosX = 1.5f,
                PosY = 2.5f,
                PosZ = -3.5f,
                Yaw = 0.25f,
                Pitch = -0.1f,
                O2Percent = 87.5f,
                HealthPercent = 66f,
                Inventory = new Dictionary<string, int> { ["scrap_metal"] = 5 },
                HandSlots = new List<SlotSaveData?>
                {
                    new SlotSaveData { ItemId = "battery", Count = 1, Charge = 0.42f },
                    null,
                },
                Credits = 20,
                BackpackItemId = "backpack",
                BackpackContents = new Dictionary<string, int> { ["wall_panel"] = 3 },
                BackpackSlots = new List<SlotSaveData?>
                {
                    new SlotSaveData { ItemId = "wall_panel", Count = 3, Charge = 1f },
                    null,
                    null,
                },
                BackpackSlotCount = 24,
                HasDrill = true,
                DrillHasBattery = true,
                DrillCharge = 0.68f,
                HasFlashlight = true,
                FlashlightHasBattery = true,
                FlashlightCharge = 0.5f,
                InventoryWindow = new WindowPosition(10f, 20f),
                DrillWindow = new WindowPosition(-30f, 40f),
                FlashlightWindow = new WindowPosition(50f, 60f),
                BackpackWindow = new WindowPosition(70f, 80f),
                HeadItemId = "eva_helmet",
                TorsoItemId = "eva_torso_suit",
                TorsoSlots = new List<SlotSaveData?>
                {
                    new SlotSaveData { ItemId = "scrap_metal", Count = 1, Charge = 1f },
                    null,
                },
                TorsoSlotCount = 2,
                HasSuitO2Tank = true,
                SuitO2Charge = 0.75f,
                HasSuitN2Tank = true,
                SuitN2Charge = 0.5f,
                HasSuitFilter = true,
                SuitFilterCharge = 0.9f,
                HasSuitBattery = true,
                SuitBatteryCharge = 0.6f,
                CO2Percent = 42f,
                SuitWindow = new WindowPosition(90f, 100f),
            },
            ObjectStates = new Dictionary<string, bool> { ["door-1"] = true },
            ObjectStringStates = new Dictionary<string, string> { ["conduit-1"] = "repaired" },
        };
        data.DroppedContainers.Add(new DroppedContainerSaveData
        {
            PosX = 1,
            PosY = 2,
            PosZ = 3,
            ItemId = "backpack",
            Contents = new Dictionary<string, int> { ["power_cell"] = 2 },
            Slots = new List<SlotSaveData?>
            {
                new SlotSaveData { ItemId = "power_cell", Count = 2, Charge = 1f },
                null,
            },
        });

        var roundTripped = JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(data));

        Assert.NotNull(roundTripped);
        Assert.Equal(data.Version, roundTripped.Version);
        Assert.Equal(data.Player.PosX, roundTripped.Player.PosX);
        Assert.Equal(data.Player.BackpackItemId, roundTripped.Player.BackpackItemId);
        Assert.Equal(data.Player.BackpackSlotCount, roundTripped.Player.BackpackSlotCount);
        Assert.Equal(data.Player.Inventory, roundTripped.Player.Inventory);
        Assert.Equal(2, roundTripped.Player.HandSlots.Count);
        Assert.Equal("battery", roundTripped.Player.HandSlots[0]!.ItemId);
        Assert.Equal(0.42f, roundTripped.Player.HandSlots[0]!.Charge);
        Assert.Null(roundTripped.Player.HandSlots[1]);
        Assert.Equal(3, roundTripped.Player.BackpackSlots.Count);
        Assert.Equal("wall_panel", roundTripped.Player.BackpackSlots[0]!.ItemId);
        Assert.Null(roundTripped.Player.BackpackSlots[1]);
        Assert.True(roundTripped.Player.HasDrill);
        Assert.True(roundTripped.Player.DrillHasBattery);
        Assert.Equal(data.Player.DrillCharge, roundTripped.Player.DrillCharge);
        Assert.Equal(data.Player.HealthPercent, roundTripped.Player.HealthPercent);
        Assert.True(roundTripped.Player.HasFlashlight);
        Assert.True(roundTripped.Player.FlashlightHasBattery);
        Assert.Equal(data.Player.FlashlightCharge, roundTripped.Player.FlashlightCharge);
        Assert.Equal(data.Player.InventoryWindow, roundTripped.Player.InventoryWindow);
        Assert.Equal(data.Player.DrillWindow, roundTripped.Player.DrillWindow);
        Assert.Equal(data.Player.FlashlightWindow, roundTripped.Player.FlashlightWindow);
        Assert.Equal(data.Player.BackpackWindow, roundTripped.Player.BackpackWindow);
        Assert.Equal(data.Player.HeadItemId, roundTripped.Player.HeadItemId);
        Assert.Equal(data.Player.TorsoItemId, roundTripped.Player.TorsoItemId);
        Assert.Equal(2, roundTripped.Player.TorsoSlots.Count);
        Assert.Equal("scrap_metal", roundTripped.Player.TorsoSlots[0]!.ItemId);
        Assert.Null(roundTripped.Player.TorsoSlots[1]);
        Assert.Equal(data.Player.TorsoSlotCount, roundTripped.Player.TorsoSlotCount);
        Assert.True(roundTripped.Player.HasSuitO2Tank);
        Assert.Equal(data.Player.SuitO2Charge, roundTripped.Player.SuitO2Charge);
        Assert.True(roundTripped.Player.HasSuitN2Tank);
        Assert.Equal(data.Player.SuitN2Charge, roundTripped.Player.SuitN2Charge);
        Assert.True(roundTripped.Player.HasSuitFilter);
        Assert.Equal(data.Player.SuitFilterCharge, roundTripped.Player.SuitFilterCharge);
        Assert.True(roundTripped.Player.HasSuitBattery);
        Assert.Equal(data.Player.SuitBatteryCharge, roundTripped.Player.SuitBatteryCharge);
        Assert.Equal(data.Player.CO2Percent, roundTripped.Player.CO2Percent);
        Assert.Equal(data.Player.SuitWindow, roundTripped.Player.SuitWindow);
        Assert.Equal(data.ObjectStates, roundTripped.ObjectStates);
        Assert.Equal(data.ObjectStringStates, roundTripped.ObjectStringStates);
        Assert.Single(roundTripped.DroppedContainers);
        Assert.Equal("backpack", roundTripped.DroppedContainers[0].ItemId);
        Assert.Equal(2, roundTripped.DroppedContainers[0].Slots.Count);
        Assert.Equal("power_cell", roundTripped.DroppedContainers[0].Slots[0]!.ItemId);
        Assert.Null(roundTripped.DroppedContainers[0].Slots[1]);
    }

    [Fact]
    public void SlotSaveDataConverter_RoundTrips_ChargeAndEmptySlotPositions()
    {
        var container = new SlotContainer(3);
        container.SetSlot(0, ("battery", 1, 0.42f));
        container.SetSlot(2, ("scrap_metal", 5, 1f));

        var captured = SlotSaveDataConverter.Capture(container);
        var restored = new SlotContainer(3);
        SlotSaveDataConverter.Restore(restored, captured);

        Assert.Equal(("battery", 1, 0.42f), restored.Slots[0]);
        Assert.Null(restored.Slots[1]);
        Assert.Equal(("scrap_metal", 5, 1f), restored.Slots[2]);
    }

    [Fact]
    public void BuildTargetSaveData_RoundTrips_EveryCollectionIncludingRecordStructDefaults()
    {
        var data = new BuildTargetSaveData();
        data.Conduits.Add(new TileCoord(1, 2));
        data.WallConduits.Add(new WallConduitCoord(1, 2, 3, 4)); // default Slot = 1
        data.Walls.Add(new EdgeCoord(0, 0, 1, 0));
        data.FloorBreaches.Add(new TileCoord(3, 0));
        data.CeilingBreaches.Add(new TileCoord(6, 5));
        data.Machines.Add(new MachineCoord("battery", 4, 0, 4, -1, "0.75"));
        data.Machines.Add(new MachineCoord("recharge_station", 9, 0, 9, -1, null));
        data.ExtendedCells.Add(new TileCoord(7, 2));

        var roundTripped = JsonSerializer.Deserialize<BuildTargetSaveData>(JsonSerializer.Serialize(data));

        Assert.NotNull(roundTripped);
        Assert.Equal(new TileCoord(1, 2), roundTripped.Conduits[0]);
        Assert.Equal(1, roundTripped.WallConduits[0].Slot);
        Assert.Equal(new EdgeCoord(0, 0, 1, 0), roundTripped.Walls[0]);
        Assert.Equal(2, roundTripped.Machines.Count);
        Assert.Equal("0.75", roundTripped.Machines[0].State);
        Assert.Null(roundTripped.Machines[1].State);
        Assert.Equal(new TileCoord(7, 2), roundTripped.ExtendedCells[0]);
    }
}
