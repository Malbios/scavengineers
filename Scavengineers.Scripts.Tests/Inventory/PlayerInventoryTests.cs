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
            ["battery"] = new() { Id = "battery", MaxStackSize = 1 },
        });
    }

    public void Dispose() => ItemCatalog.ResetForTests();

    [Fact]
    public void HasAny_FindsAMatchInAHandSlotOrTheBackpack_AndFalseWhenNoSlotMatches()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));
        inventory.Backpack!.Contents.Add("battery", 1);

        Assert.True(inventory.HasAny(itemId => itemId == "battery"));
        Assert.False(inventory.HasAny(itemId => itemId == "widget"));

        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

        Assert.True(inventory.HasAny(itemId => itemId == "widget"));
    }

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
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

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
        inventory.Backpack!.Contents.SetSlot(0, ("widget", 1, 1f)); // fill the only backpack slot
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

        var result = inventory.Unequip(PlayerInventory.LeftHandSlotIndex);

        Assert.False(result);
        Assert.Equal("widget", inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]!.Value.ItemId);
    }

    [Fact]
    public void Unequip_MovesHandContentsIntoTheBackpack_WhenRoomExists()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("backpack", new SlotContainer(2)));
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

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
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("backpack", 1, 1f));

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

    [Fact]
    public void EjectDrillBattery_PreservesItsRealChargeInTheResultingItem()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: true, charge: 0.42f);

        Assert.True(inventory.EjectDrillBattery());

        Assert.False(inventory.Drill!.HasBattery);
        var ejected = inventory.Hands.Slots.First(s => s?.ItemId == "battery");
        Assert.Equal(0.42f, ejected!.Value.Charge);
    }

    [Fact]
    public void InsertDrillBattery_HonorsTheSpareBatterysRealCharge_NotRefillingToFull()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: true, charge: 0.42f);
        inventory.EjectDrillBattery(); // drill now empty; a 42%-charged battery sits in a hand

        Assert.True(inventory.InsertDrillBattery());

        Assert.Equal(0.42f, inventory.Drill!.Charge);
    }

    [Fact]
    public void EjectDrillBatteryForWorld_ReturnsRealChargeAndClearsTheDrillsBattery_WithNoInventoryDestination()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: true, charge: 0.42f);

        var charge = inventory.EjectDrillBatteryForWorld();

        Assert.Equal(0.42f, charge);
        Assert.False(inventory.Drill!.HasBattery);
        Assert.Equal(0, inventory.CountOf("battery")); // nothing landed in a hand/backpack slot
    }

    [Fact]
    public void EjectDrillBatteryForWorld_ReturnsNull_WhenNoBatteryIsInstalled()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: false, charge: 0f);

        Assert.Null(inventory.EjectDrillBatteryForWorld());
    }

    [Fact]
    public void EjectFlashlightBatteryForWorld_ReturnsRealChargeAndClearsTheFlashlightsBattery_WithNoInventoryDestination()
    {
        var inventory = new PlayerInventory();
        inventory.AttachFlashlight(hasBattery: true, charge: 0.7f);

        var charge = inventory.EjectFlashlightBatteryForWorld();

        Assert.Equal(0.7f, charge);
        Assert.False(inventory.Flashlight!.HasBattery);
        Assert.Equal(0, inventory.CountOf("battery"));
    }

    [Fact]
    public void EjectFlashlightBattery_PreservesItsRealChargeInTheResultingItem()
    {
        var inventory = new PlayerInventory();
        inventory.AttachFlashlight(hasBattery: true, charge: 0.7f);

        Assert.True(inventory.EjectFlashlightBattery());

        Assert.False(inventory.Flashlight!.HasBattery);
        var ejected = inventory.Hands.Slots.First(s => s?.ItemId == "battery");
        Assert.Equal(0.7f, ejected!.Value.Charge);
    }

    [Fact]
    public void InsertFlashlightBattery_HonorsTheSpareBatterysRealCharge_NotRefillingToFull()
    {
        var inventory = new PlayerInventory();
        inventory.AttachFlashlight(hasBattery: true, charge: 0.7f);
        inventory.EjectFlashlightBattery(); // flashlight now empty; a 70%-charged battery sits in a hand

        Assert.True(inventory.InsertFlashlightBattery());

        Assert.Equal(0.7f, inventory.Flashlight!.Charge);
    }

    [Fact]
    public void EjectDrillBatteryTo_MovesTheRealChargeIntoTheGivenSlot_AndClearsTheDrill()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: true, charge: 0.42f);
        var container = new SlotContainer(1);

        Assert.True(inventory.EjectDrillBatteryTo(container, 0));

        Assert.False(inventory.Drill!.HasBattery);
        Assert.Equal(("battery", 1, 0.42f), container.Slots[0]);
    }

    [Fact]
    public void EjectDrillBatteryTo_ReturnsFalse_WhenTheTargetSlotIsAlreadyOccupied()
    {
        var inventory = new PlayerInventory();
        inventory.AttachDrill(hasBattery: true, charge: 0.42f);
        var container = new SlotContainer(1);
        container.SetSlot(0, ("widget", 1, 1f));

        Assert.False(inventory.EjectDrillBatteryTo(container, 0));

        Assert.True(inventory.Drill!.HasBattery); // untouched — the eject never happened
    }

    [Fact]
    public void EjectFlashlightBatteryTo_MovesTheRealChargeIntoTheGivenSlot_AndClearsTheFlashlight()
    {
        var inventory = new PlayerInventory();
        inventory.AttachFlashlight(hasBattery: true, charge: 0.7f);
        var container = new SlotContainer(1);

        Assert.True(inventory.EjectFlashlightBatteryTo(container, 0));

        Assert.False(inventory.Flashlight!.HasBattery);
        Assert.Equal(("battery", 1, 0.7f), container.Slots[0]);
    }
}
