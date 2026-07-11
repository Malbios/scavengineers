using System.Text.Json;
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
                PowerPercent = 42f,
                Inventory = new Dictionary<string, int> { ["scrap_metal"] = 5 },
                Credits = 20,
                BackpackItemId = "backpack",
                BackpackContents = new Dictionary<string, int> { ["wall_panel"] = 3 },
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
        });

        var roundTripped = JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(data));

        Assert.NotNull(roundTripped);
        Assert.Equal(data.Version, roundTripped.Version);
        Assert.Equal(data.Player.PosX, roundTripped.Player.PosX);
        Assert.Equal(data.Player.BackpackItemId, roundTripped.Player.BackpackItemId);
        Assert.Equal(data.Player.Inventory, roundTripped.Player.Inventory);
        Assert.Equal(data.ObjectStates, roundTripped.ObjectStates);
        Assert.Equal(data.ObjectStringStates, roundTripped.ObjectStringStates);
        Assert.Single(roundTripped.DroppedContainers);
        Assert.Equal("backpack", roundTripped.DroppedContainers[0].ItemId);
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

        var roundTripped = JsonSerializer.Deserialize<BuildTargetSaveData>(JsonSerializer.Serialize(data));

        Assert.NotNull(roundTripped);
        Assert.Equal(new TileCoord(1, 2), roundTripped.Conduits[0]);
        Assert.Equal(1, roundTripped.WallConduits[0].Slot);
        Assert.Equal(new EdgeCoord(0, 0, 1, 0), roundTripped.Walls[0]);
        Assert.Equal(2, roundTripped.Machines.Count);
        Assert.Equal("0.75", roundTripped.Machines[0].State);
        Assert.Null(roundTripped.Machines[1].State);
    }
}
