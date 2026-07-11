using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Tests.Inventory;

[Collection(ItemCatalogCollection.Name)]
public class SlotContainerTests : IDisposable
{
    public SlotContainerTests()
    {
        ItemCatalog.SeedForTests(new Dictionary<string, ItemCatalog.ItemDefinition>
        {
            ["scrap"] = new() { Id = "scrap", MaxStackSize = 3 },
            ["wall_panel"] = new() { Id = "wall_panel", MaxStackSize = 1 },
        });
    }

    public void Dispose() => ItemCatalog.ResetForTests();

    [Fact]
    public void Add_FillsAcrossMultipleSlots_RespectingStackLimit()
    {
        var container = new SlotContainer(2);

        var added = container.Add("scrap", 5);

        Assert.Equal(5, added);
        Assert.Equal((3, 2), (container.Slots[0]!.Value.Count, container.Slots[1]!.Value.Count));
    }

    [Fact]
    public void Add_ReturnsPartialFitCount_WhenAtCapacity()
    {
        var container = new SlotContainer(1);

        var added = container.Add("scrap", 5);

        Assert.Equal(3, added);
        Assert.Equal(3, container.CountOf("scrap"));
    }

    [Fact]
    public void MoveSlot_SwapsDifferentItems()
    {
        var container = new SlotContainer(2);
        container.Add("scrap", 1);
        container.SetSlot(1, ("wall_panel", 1));

        container.MoveSlot(0, 1);

        Assert.Equal("wall_panel", container.Slots[0]!.Value.ItemId);
        Assert.Equal("scrap", container.Slots[1]!.Value.ItemId);
    }

    [Fact]
    public void MoveSlot_MergesSameItem_LeavingRemainderInSource()
    {
        var container = new SlotContainer(2);
        container.SetSlot(0, ("scrap", 2));
        container.SetSlot(1, ("scrap", 2));

        container.MoveSlot(0, 1);

        Assert.Equal((3, 1), (container.Slots[1]!.Value.Count, container.Slots[0]!.Value.Count));
    }

    [Fact]
    public void MoveBetween_DifferentContainers_MergesSameItem()
    {
        var from = new SlotContainer(1);
        var to = new SlotContainer(1);
        from.SetSlot(0, ("scrap", 2));
        to.SetSlot(0, ("scrap", 2));

        SlotContainer.MoveBetween(from, 0, to, 0);

        Assert.Equal(3, to.Slots[0]!.Value.Count);
        Assert.Equal(1, from.Slots[0]!.Value.Count);
    }

    [Fact]
    public void TryRemove_DrawsAcrossMultipleSlots()
    {
        var container = new SlotContainer(2);
        container.Add("scrap", 5); // fills slot 0 to 3, slot 1 to 2

        var removed = container.TryRemove("scrap", 4);

        Assert.True(removed);
        Assert.Equal(1, container.CountOf("scrap"));
    }

    [Fact]
    public void RoomFor_ReflectsFreeSlotsAndPartialStacks()
    {
        var container = new SlotContainer(2);
        container.SetSlot(0, ("scrap", 1)); // 2 more room here, plus a whole empty slot (3)

        Assert.Equal(5, container.RoomFor("scrap"));
        Assert.True(container.HasRoomFor("scrap", 5));
        Assert.False(container.HasRoomFor("scrap", 6));
    }
}
