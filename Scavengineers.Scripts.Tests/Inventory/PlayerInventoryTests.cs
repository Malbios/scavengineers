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
            ["o2_tank"] = new() { Id = "o2_tank", MaxStackSize = 1 },
        });
    }

    public void Dispose() => ItemCatalog.ResetForTests();

    [Fact]
    public void HasAny_FindsAMatchInAHandSlotOrTheBackpack_AndFalseWhenNoSlotMatches()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));
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
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));

        var added = inventory.Add("widget", 3);

        Assert.Equal(3, added);
        Assert.Equal(2, inventory.Backpack!.Contents.CountOf("widget"));
        Assert.Equal(1, inventory.Hands.CountOf("widget"));
    }

    [Fact]
    public void HasRoomFor_CombinesWornBackpackAndHandCapacity()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));

        Assert.True(inventory.HasRoomFor("widget", 4)); // 2 backpack + 2 hands
        Assert.False(inventory.HasRoomFor("widget", 5));
    }

    [Fact]
    public void TryRemove_PrefersTheWornBackpackOverAHeldItem()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));
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
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));
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
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(1)));
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
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2)));
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
    public void EquipContainerDirectly_FailsWhenThatSlotIsAlreadyOccupied()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("back", "backpack-a", new SlotContainer(1)));

        var second = inventory.EquipContainerDirectly("back", "backpack-b", new SlotContainer(1));

        Assert.False(second);
        Assert.Equal("backpack-a", inventory.Backpack!.ItemId);
    }

    [Fact]
    public void TwoSimultaneouslyEquippedContainers_BothAggregateInto_AddCountOfAndCounts()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(1)));
        Assert.True(inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(1)));

        // Back is filled first per ContainerPriority; the second widget must spill into torso,
        // not hands, since torso still has room — this is the actual behavior under test, not
        // just "both exist."
        var added = inventory.Add("widget", 2);

        Assert.Equal(2, added);
        Assert.Equal(1, inventory.Backpack!.Contents.CountOf("widget"));
        Assert.Equal(1, inventory.Torso!.Contents.CountOf("widget"));
        Assert.Equal(2, inventory.CountOf("widget"));
        Assert.Equal(2, inventory.Counts["widget"]);
        Assert.Equal(0, inventory.Hands.CountOf("widget"));
    }

    [Fact]
    public void EjectSpecializedSlot_PreservesItsRealChargeInTheResultingItem()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.42f);

        Assert.True(inventory.EjectSpecializedSlot("drill_battery"));

        Assert.False(inventory.Drill!.HasItem);
        var ejected = inventory.Hands.Slots.First(s => s?.ItemId == "battery");
        Assert.Equal(0.42f, ejected!.Value.Charge);
    }

    [Fact]
    public void InsertIntoSpecializedSlot_HonorsTheSpareItemsRealCharge_NotRefillingToFull()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.42f);
        inventory.EjectSpecializedSlot("drill_battery"); // drill now empty; a 42%-charged battery sits in a hand

        Assert.True(inventory.InsertIntoSpecializedSlot("drill_battery"));

        Assert.Equal(0.42f, inventory.Drill!.Charge);
    }

    [Fact]
    public void EjectSpecializedSlotForWorld_ReturnsRealChargeAndClearsTheSlot_WithNoInventoryDestination()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.42f);

        var charge = inventory.EjectSpecializedSlotForWorld("drill_battery");

        Assert.Equal(0.42f, charge);
        Assert.False(inventory.Drill!.HasItem);
        Assert.Equal(0, inventory.CountOf("battery")); // nothing landed in a hand/backpack slot
    }

    [Fact]
    public void EjectSpecializedSlotForWorld_ReturnsNull_WhenNoItemIsInstalled()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: false, charge: 0f);

        Assert.Null(inventory.EjectSpecializedSlotForWorld("drill_battery"));
    }

    [Fact]
    public void EjectSpecializedSlotForWorld_WorksIdenticallyForTheFlashlightSlot()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 0.7f);

        var charge = inventory.EjectSpecializedSlotForWorld("flashlight_battery");

        Assert.Equal(0.7f, charge);
        Assert.False(inventory.Flashlight!.HasItem);
        Assert.Equal(0, inventory.CountOf("battery"));
    }

    [Fact]
    public void EjectSpecializedSlot_PreservesItsRealCharge_ForTheFlashlightSlotToo()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 0.7f);

        Assert.True(inventory.EjectSpecializedSlot("flashlight_battery"));

        Assert.False(inventory.Flashlight!.HasItem);
        var ejected = inventory.Hands.Slots.First(s => s?.ItemId == "battery");
        Assert.Equal(0.7f, ejected!.Value.Charge);
    }

    [Fact]
    public void InsertIntoSpecializedSlot_HonorsTheSpareItemsRealCharge_ForTheFlashlightSlotToo()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 0.7f);
        inventory.EjectSpecializedSlot("flashlight_battery"); // flashlight now empty; a 70%-charged battery sits in a hand

        Assert.True(inventory.InsertIntoSpecializedSlot("flashlight_battery"));

        Assert.Equal(0.7f, inventory.Flashlight!.Charge);
    }

    [Fact]
    public void EjectSpecializedSlotTo_MovesTheRealChargeIntoTheGivenSlot_AndClearsTheDrill()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.42f);
        var container = new SlotContainer(1);

        Assert.True(inventory.EjectSpecializedSlotTo("drill_battery", container, 0));

        Assert.False(inventory.Drill!.HasItem);
        Assert.Equal(("battery", 1, 0.42f), container.Slots[0]);
    }

    [Fact]
    public void EjectSpecializedSlotTo_ReturnsFalse_WhenTheTargetSlotIsAlreadyOccupied()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.42f);
        var container = new SlotContainer(1);
        container.SetSlot(0, ("widget", 1, 1f));

        Assert.False(inventory.EjectSpecializedSlotTo("drill_battery", container, 0));

        Assert.True(inventory.Drill!.HasItem); // untouched — the eject never happened
    }

    [Fact]
    public void EjectSpecializedSlotTo_MovesTheRealChargeIntoTheGivenSlot_ForTheFlashlightSlotToo()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 0.7f);
        var container = new SlotContainer(1);

        Assert.True(inventory.EjectSpecializedSlotTo("flashlight_battery", container, 0));

        Assert.False(inventory.Flashlight!.HasItem);
        Assert.Equal(("battery", 1, 0.7f), container.Slots[0]);
    }

    [Fact]
    public void DrainSpecializedSlot_ClampsAtZero_AndIsANoOp_WhenTheSlotDoesNotExist()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 0.1f);

        inventory.DrainSpecializedSlot("drill_battery", 0.5f);
        Assert.Equal(0f, inventory.Drill!.Charge);

        inventory.DrainSpecializedSlot("nonexistent_slot", 0.5f); // no-op, no exception
    }

    [Fact]
    public void SuitO2Slot_InsertAndEject_RoundTripsThroughTheSameGenericMechanismAsDrill()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("suit_o2", hasItem: false, charge: 0f); // "torso is worn, tank slot empty"
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("o2_tank", 1, 0.88f));

        Assert.True(inventory.InsertIntoSpecializedSlot("suit_o2"));
        Assert.True(inventory.SuitO2!.HasItem);
        Assert.Equal(0.88f, inventory.SuitO2.Charge);

        Assert.True(inventory.EjectSpecializedSlot("suit_o2"));
        Assert.False(inventory.SuitO2!.HasItem);
        var ejected = inventory.Hands.Slots.First(s => s?.ItemId == "o2_tank");
        Assert.Equal(0.88f, ejected!.Value.Charge);
    }

    [Fact]
    public void InsertIntoSpecializedSlot_FailsForSuitSlots_WhenTheSuitIsntWorn()
    {
        var inventory = new PlayerInventory(); // no AttachSpecializedSlot("suit_o2", ...) — torso never equipped
        inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("o2_tank", 1, 1f));

        Assert.False(inventory.InsertIntoSpecializedSlot("suit_o2"));
        Assert.Null(inventory.SuitO2);
    }

    [Fact]
    public void DetachSpecializedSlot_SetsItFullyBackToNull_NotJustEmpty()
    {
        var inventory = new PlayerInventory();
        inventory.AttachSpecializedSlot("suit_n2", hasItem: true, charge: 0.5f);

        inventory.DetachSpecializedSlot("suit_n2");

        Assert.Null(inventory.SuitN2);
    }
}
