using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the new generalized Torso/Head equip-slot flow
/// (Player.TryEquipItemFromHand/TryUnequipItem) and the Legs/LeftFoot/RightFoot "blocked while
/// the EVA suit's torso is worn" gate. Equips directly via PlayerInventory.EquipContainerDirectly
/// rather than exercising TryEquipItemFromHand's own ItemCatalog.EquipSlot gate — this project's
/// isolated NodeTests res:// has no Data/items.json (see PlayerTestHarness's own doc comment), so
/// ItemCatalog.EquipSlot always returns null here regardless of item id; that specific gate is
/// covered instead by ItemCatalogTests.EquipSlot_ReturnsTheDeclaredSlot (Scavengineers.Scripts.Tests,
/// where ItemCatalog.SeedForTests actually works) plus manual playtest.</summary>
[TestSuite]
public class PlayerEquipSlotTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_ReturnsAnEmptyItemToAHand_WhenRoomExists()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        // The harness's fresh-game stipend really runs in _Ready() — in this project's isolated,
        // catalog-less NodeTests environment (see PlayerTestHarness's own doc comment), every
        // item's MaxStackSize falls back to 1, so the 50-count scrap_metal stipend overflows past
        // the 24-slot debug backpack into both bare hands. Clear them explicitly so this test's
        // own "room exists" precondition is real, not accidentally already-full from that.
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));

        player.TryUnequipItem("torso");

        AssertBool(player.Inventory.Torso is null).IsTrue();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "eva_torso_suit")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_DropsANonEmptyItemInTheWorld_WhenBothHandsAreFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));

        // This project's isolated NodeTests catalog can't load real Data/items.json, so every
        // item's MaxStackSize falls back to 1 (see PlayerTestHarness's own doc comment) — a
        // single slot holding "3 scrap_metal" isn't reachable here; one real item in one slot is
        // enough to prove "non-empty container survives the drop intact."
        var contents = new SlotContainer(2);
        contents.Add("scrap_metal", 1);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", contents);

        player.TryUnequipItem("torso");

        // Both hands stay full (item couldn't fall back into either) — dropped in the world
        // instead, exactly like TryUnequipBackpack's own full-container behavior.
        AssertBool(player.Inventory.Torso is null).IsTrue();
        var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "eva_torso_suit");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(dropped!.Contents!.CountOf("scrap_metal") == 1).IsTrue();

        AutoFree(dropped);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_IsANoOp_WhenNothingIsEquippedInThatSlot()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        player.TryUnequipItem("torso"); // no exception, nothing to do

        AssertBool(player.Inventory.Torso is null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void UnusedBodySlot_RejectsEveryDrop_RegardlessOfWhetherTheTorsoIsWorn()
    {
        var legSlot = AutoFree(new InventorySlotUI { IsUnusedBodySlot = true });
        var source = AutoFree(new InventorySlotUI());

        AssertBool(legSlot!._CanDropData(Vector2.Zero, Variant.From(source))).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void OrdinarySlot_StillAcceptsDrops_ForContrast()
    {
        var ordinarySlot = AutoFree(new InventorySlotUI());
        var source = AutoFree(new InventorySlotUI());

        AssertBool(ordinarySlot!._CanDropData(Vector2.Zero, Variant.From(source))).IsTrue();
    }
}
