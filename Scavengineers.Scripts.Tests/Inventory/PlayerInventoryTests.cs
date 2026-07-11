using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Tests.Inventory;

[Collection(ItemCatalogCollection.Name)]
public class PlayerInventoryTests : IDisposable
{
    public PlayerInventoryTests()
    {
        ItemCatalog.SeedForTests(new Dictionary<string, ItemCatalog.ItemDefinition>
        {
            ["widget"] = new() { Id = "widget", MaxStackSize = 1 },
            ["backpack"] = new() { Id = "backpack", MaxStackSize = 1 },
        });
    }

    public void Dispose() => ItemCatalog.ResetForTests();

    [Fact]
    public void Add_ReturnsPartialFit_WhenNoBackpackAndHandsFull()
    {
        var inventory = new PlayerInventory();

        var added = inventory.Add("widget", 5);

        Assert.Equal(PlayerInventory.HandCount, added);
        Assert.Equal(PlayerInventory.HandCount, inventory.CountOf("widget"));
    }

    [Fact]
    public void Add_FillsTheWornBackpackFirst_ThenSpillsIntoHands()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));

        var added = inventory.Add("widget", 3);

        Assert.Equal(3, added);
        Assert.Equal(2, inventory.Backpack!.Contents.CountOf("widget"));
        Assert.Equal(1, inventory.Hands.CountOf("widget"));
    }

    [Fact]
    public void HasRoomFor_CombinesWornBackpackAndHandCapacity()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));

        Assert.True(inventory.HasRoomFor("widget", 4)); // 2 backpack + 2 hands
        Assert.False(inventory.HasRoomFor("widget", 5));
    }

    [Fact]
    public void TryRemove_PrefersTheWornBackpackOverAHeldItem()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));
        inventory.Backpack!.Contents.Add("widget", 2);
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1));

        var removed = inventory.TryRemove("widget", 2);

        Assert.True(removed);
        Assert.Equal(0, inventory.Backpack.Contents.CountOf("widget"));
        Assert.Equal(1, inventory.Hands.CountOf("widget")); // held item drained last, untouched here
    }

    [Fact]
    public void Equip_MovesAnItemFromTheBackpackIntoAHand()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));
        inventory.Backpack!.Contents.Add("widget", 1);

        var equipped = inventory.Equip("widget", PlayerInventory.LeftHandSlotIndex);

        Assert.True(equipped);
        Assert.Equal("widget", inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]!.Value.ItemId);
        Assert.Equal(0, inventory.Backpack.Contents.CountOf("widget"));
    }

    [Fact]
    public void Equip_ReturnsFalse_WhenNoBackpackIsWorn()
    {
        var inventory = new PlayerInventory();

        Assert.False(inventory.Equip("widget", PlayerInventory.LeftHandSlotIndex));
    }

    [Fact]
    public void Unequip_ReturnsFalseAndKeepsHandContents_WhenBackpackHasNoRoom()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(1)));
        inventory.Backpack!.Contents.SetSlot(0, ("widget", 1)); // fill the only backpack slot
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1));

        var result = inventory.Unequip(PlayerInventory.LeftHandSlotIndex);

        Assert.False(result);
        Assert.Equal("widget", inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]!.Value.ItemId);
    }

    [Fact]
    public void Unequip_MovesHandContentsIntoTheBackpack_WhenRoomExists()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1));

        var result = inventory.Unequip(PlayerInventory.LeftHandSlotIndex);

        Assert.True(result);
        Assert.Null(inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]);
        Assert.Equal(1, inventory.Backpack!.Contents.CountOf("widget"));
    }

    [Fact]
    public void Unequip_IsANoOp_WhenTheHandIsAlreadyEmpty()
    {
        var inventory = new PlayerInventory();

        Assert.True(inventory.Unequip(PlayerInventory.LeftHandSlotIndex));
    }

    [Fact]
    public void EquipBackpackFromHand_ConsumesTheBackpackItem_AndClearBackpackDetachesIt()
    {
        var inventory = new PlayerInventory();
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("backpack", 1));

        var equipped = inventory.EquipBackpackFromHand();

        Assert.True(equipped);
        Assert.NotNull(inventory.Backpack);
        Assert.Equal("backpack", inventory.Backpack!.ItemId);
        Assert.Equal(PlayerInventory.BackpackSlotCount, inventory.Backpack.Contents.Slots.Count);
        Assert.Null(inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]);
        Assert.False(inventory.EquipBackpackFromHand()); // already worn

        inventory.ClearBackpack();

        Assert.Null(inventory.Backpack);
    }

    [Fact]
    public void EquipContainerDirectly_FailsWhenABackpackIsAlreadyWorn()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack-a", new SlotContainer(1)));

        var second = inventory.EquipContainerDirectly("backpack-b", new SlotContainer(1));

        Assert.False(second);
        Assert.Equal("backpack-a", inventory.Backpack!.ItemId);
    }
}
