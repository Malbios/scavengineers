using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the new generalized Torso/Head equip-slot flow
/// (Player.TryEquipItemFrom/TryUnequipItem) and the Legs/LeftFoot/RightFoot "blocked while
/// the EVA suit's torso is worn" gate. Equips directly via PlayerInventory.EquipContainerDirectly
/// rather than exercising TryEquipItemFrom's own ItemCatalog.EquipSlot gate — this project's
/// isolated NodeTests res:// has no Data/items.json (see PlayerTestHarness's own doc comment), so
/// ItemCatalog.EquipSlot always returns null here regardless of item id; that specific gate is
/// covered instead by ItemCatalogTests.EquipSlot_ReturnsTheDeclaredSlot (Scavengineers.Scripts.Tests,
/// where ItemCatalog.SeedForTests actually works) plus manual playtest.</summary>
[TestSuite]
public class PlayerEquipSlotTest
{
    /// <summary>The harness's fresh-game stipend really runs in _Ready() and now includes a
    /// starter EVA suit (torso+helmet, fully suited) — undo that first so a test that wants to
    /// exercise its own from-scratch equip/unequip scenario isn't fighting the stipend's own
    /// already-worn suit. Also discards the stipend's persistent-contents entries for the suit
    /// pieces (not just unwearing them) — under the persistent-contents model, an item's inner
    /// contents survive being unworn, so a test that re-equips "eva_torso_suit" with its own
    /// fresh container would otherwise silently get the stipend's leftover (empty) one back.</summary>
    private static void ResetTorsoAndHeadFromStipend(PlayerInventory inventory)
    {
        var torsoItemId = inventory.Torso?.ItemId;
        var headItemId = inventory.Head?.ItemId;
        inventory.ClearEquippedContainer("torso");
        inventory.ClearEquippedContainer("head");
        if (torsoItemId is not null) inventory.DiscardPersistentContents(torsoItemId);
        if (headItemId is not null) inventory.DiscardPersistentContents(headItemId);
        inventory.DetachSpecializedSlot("suit_o2");
        inventory.DetachSpecializedSlot("suit_n2");
        inventory.DetachSpecializedSlot("suit_filter");
        inventory.DetachSpecializedSlot("suit_battery");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_ReturnsAnEmptyItemToAHand_WhenRoomExists()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        // The harness's fresh-game stipend also loads up the debug backpack/hands — in this
        // project's isolated, catalog-less NodeTests environment (see PlayerTestHarness's own
        // doc comment), every item's MaxStackSize falls back to 1, so the 50-count scrap_metal
        // stipend overflows past the 24-slot debug backpack into both bare hands. Clear them
        // explicitly so this test's own "room exists" precondition is real, not accidentally
        // already-full from that.
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));

        player.TryUnequipItem("torso");

        AssertBool(player.Inventory.Torso is null).IsTrue();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "eva_torso_suit")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_StaysEquipped_WhenBothHandsAreFullAndNoDestinationGiven_EvenWithNonEmptyPocketContents()
    {
        // Under the persistent-contents model, a worn item's contents never travel with the
        // unequip decision (see PlayerInventory.GetPersistentContents) — so unlike the old
        // "non-empty container forces a world drop" behavior, both hands being full with no
        // drop destination now just means the suit can't come off right now and stays worn,
        // same "nothing vanishes" contract as an empty one.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));
        var contents = new SlotContainer(2);
        contents.Add("scrap_metal", 1);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", contents);

        player.TryUnequipItem("torso");

        AssertBool(player.Inventory.Torso is { ItemId: "eva_torso_suit" }).IsTrue();
        AssertBool(player.Inventory.Torso!.Contents.CountOf("scrap_metal") == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_LeavesInstalledSuitTanksAttachedAndLoaded_TanksStayWithTheSuit()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));
        // Simulates what TryEquipItemFrom's own torso-specific glue does on a real equip —
        // exercised directly here since that path needs a real ItemCatalog.EquipSlot match,
        // unreachable in this project's isolated NodeTests catalog (see class doc comment).
        player.Inventory.AttachSpecializedSlot("suit_o2", hasItem: true, charge: 0.6f);
        player.Inventory.AttachSpecializedSlot("suit_n2", hasItem: false, charge: 0f);
        player.Inventory.AttachSpecializedSlot("suit_filter", hasItem: false, charge: 0f);
        player.Inventory.AttachSpecializedSlot("suit_battery", hasItem: false, charge: 0f);

        player.TryUnequipItem("torso");

        // The suit came off into a hand, but its tank state is per-suit persistent state (like
        // its pocket contents) — decoupled from worn state, so it stays attached and loaded
        // instead of being ejected loose into general inventory.
        AssertBool(player.Inventory.Torso is null).IsTrue();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "eva_torso_suit")).IsTrue();
        AssertBool(player.Inventory.SuitO2 is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitO2!.Charge, 0.6f)).IsTrue();
        AssertBool(player.Inventory.SuitN2 is { HasItem: false }).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_PlacesTheBareTokenAtTheActualDropDestination_NotJustAHand()
    {
        // The FitsInStorage-based "suit can't go in a backpack" restriction itself is covered by
        // ItemCatalogTests (Scavengineers.Scripts.Tests, where SeedForTests actually configures
        // it) plus manual playtest — this project's isolated NodeTests catalog has no real
        // Data/items.json, so FitsInStorage always returns its "unknown item" default (true)
        // here regardless of item id (see this class's own doc comment). The helmet fits
        // everywhere either way, so it's a valid destination-respecting case regardless of that
        // limitation.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.EquipContainerDirectly("head", "eva_helmet", new SlotContainer(0));

        // A standalone container, not the (stipend-filled) real backpack — keeps this test's
        // "is this exact slot empty" precondition isolated from stipend noise.
        var destinationContainer = new SlotContainer(2);
        var destination = AutoFree(new InventorySlotUI { Container = destinationContainer, SlotIndex = 0 });

        player.TryUnequipItem("head", destination);

        AssertBool(player.Inventory.Head is null).IsTrue();
        AssertBool(destinationContainer.Slots[0]?.ItemId == "eva_helmet").IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_FallsBackToAHand_WhenTheDropDestinationIsAlreadyOccupied()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("head", "eva_helmet", new SlotContainer(0));

        var destinationContainer = new SlotContainer(2);
        destinationContainer.SetSlot(0, ("widget", 1, 1f));
        var destination = AutoFree(new InventorySlotUI { Container = destinationContainer, SlotIndex = 0 });

        player.TryUnequipItem("head", destination);

        AssertBool(player.Inventory.Head is null).IsTrue();
        AssertBool(destinationContainer.Slots[0]?.ItemId == "widget").IsTrue();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "eva_helmet")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_LeavesTheSuitsSubSlotsUntouched_WhenBothHandsAreFullAndTheTorsoStaysWorn()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        // Both hands genuinely full — the torso's own 2 pocket slots are empty, so the unequip
        // attempt reaches the "isEmpty, but nowhere for it to go" branch and re-equips instead of
        // actually coming off.
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));
        player.Inventory.AttachSpecializedSlot("suit_o2", hasItem: true, charge: 0.6f);

        player.TryUnequipItem("torso");

        // The suit never actually left, so its sub-slots must be exactly as they were —
        // regressing this would silently strip a worn suit's tanks any time both hands
        // happen to be full at the moment you tried (and failed) to take it off.
        AssertBool(player.Inventory.Torso is { ItemId: "eva_torso_suit" }).IsTrue();
        AssertBool(player.Inventory.SuitO2 is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitO2!.Charge, 0.6f)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_IsANoOp_WhenNothingIsEquippedInThatSlot()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);

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

    [TestCase]
    [RequireGodotRuntime]
    public async Task UnusedBodySlot_NeverDisplaysAnItem_EvenWhenTheSharedHandsContainerHasSomethingAtIndexZero()
    {
        // Regression test: Player._Ready's blanket EquipSlots loop points every child's Container
        // at _inventory.Hands (harmless for Torso/Head/SpecializedSlot-driven slots, which check
        // their own PlayerRef-based state first) — but Legs/LeftFoot/RightFoot never set their
        // own SlotIndex, so it defaults to 0. Before the fix, CurrentSlot() had no IsUnusedBodySlot
        // guard and fell through to reading Hands.Slots[0] directly, showing whatever's in the
        // player's left hand on a slot that should always be empty.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

        var legSlot = AutoFree(new InventorySlotUI { IsUnusedBodySlot = true, PlayerRef = player, Container = player.Inventory.Hands });
        legSlot!.AddChild(new ColorRect { Name = "Icon" });
        legSlot.AddChild(new Label { Name = "Count" });
        sceneTree.Root.AddChild(legSlot);

        // Two-step await (matches InteriorDoorVerbTargetTest's established convention) — a single
        // ProcessFrame isn't always enough for _Ready() to have wired _icon/_countLabel and for
        // _Process's own Refresh() to have run by the time we check.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var icon = legSlot.GetNode<ColorRect>("Icon");
        AssertBool(icon.Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_EquippedTorsoSlot_SpawnsTheSuitAsAWorldPickup_NotWhateverIsInTheLeftHand()
    {
        // Regression test: TryDropInWorld had no EquippedSlotName check, so dragging a worn
        // Torso/Head item into open space fell through to the generic Container/SlotIndex path
        // (Hands/0) and would silently drop/clear whatever was in the left hand instead.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        var contents = new SlotContainer(2);
        contents.Add("scrap_metal", 1);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", contents);

        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var source = AutoFree(new InventorySlotUI { EquippedSlotName = "torso" });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        AssertBool(player.Inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]?.ItemId == "widget").IsTrue();
        AssertBool(player.Inventory.Torso is null).IsTrue();
        var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "eva_torso_suit");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(dropped!.Contents!.CountOf("scrap_metal") == 1).IsTrue();

        AutoFree(dropped);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_EquippedTorsoSlot_CarriesTheSuitsTanks_RestoredExactlyOnPickup()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));
        player.Inventory.AttachSpecializedSlot("suit_o2", hasItem: true, charge: 0.6f);
        player.Inventory.AttachSpecializedSlot("suit_n2", hasItem: false, charge: 0f);
        player.Inventory.AttachSpecializedSlot("suit_filter", hasItem: true, charge: 0.9f);
        player.Inventory.AttachSpecializedSlot("suit_battery", hasItem: true, charge: 0.4f);

        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var source = AutoFree(new InventorySlotUI { EquippedSlotName = "torso" });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        // Genuinely gone from the player — a mere unequip would leave these attached (see
        // TryUnequipItem_LeavesInstalledSuitTanksAttachedAndLoaded_TanksStayWithTheSuit above);
        // dropping in the world is the one path that actually removes them.
        AssertBool(player.Inventory.SuitO2 is null).IsTrue();
        AssertBool(player.Inventory.SuitN2 is null).IsTrue();
        AssertBool(player.Inventory.SuitFilter is null).IsTrue();
        AssertBool(player.Inventory.SuitBattery is null).IsTrue();

        var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "eva_torso_suit");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(dropped!.EquipSlotName == "torso").IsTrue();
        AssertBool(dropped.SuitO2 == (true, 0.6f)).IsTrue();
        AssertBool(dropped.SuitN2 == (false, 0f)).IsTrue();
        AssertBool(dropped.SuitFilter == (true, 0.9f)).IsTrue();
        AssertBool(dropped.SuitBattery == (true, 0.4f)).IsTrue();

        // Pick it back up — its own ExecuteVerb both re-equips the torso and restores the tanks.
        dropped.ExecuteVerb(new Scavengineers.Scripts.Verbs.Verb("pick_up", "VERB_PICK_UP", DurationSeconds: 0f), player.Inventory);

        AssertBool(player.Inventory.Torso is { ItemId: "eva_torso_suit" }).IsTrue();
        AssertBool(player.Inventory.SuitO2 is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitO2!.Charge, 0.6f)).IsTrue();
        AssertBool(player.Inventory.SuitN2 is { HasItem: false }).IsTrue();
        AssertBool(player.Inventory.SuitFilter is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitFilter!.Charge, 0.9f)).IsTrue();
        AssertBool(player.Inventory.SuitBattery is { HasItem: true }).IsTrue();
        AssertBool(Mathf.IsEqualApprox(player.Inventory.SuitBattery!.Charge, 0.4f)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_BareBackpackTokenInAHand_CarriesItsPersistentContentsIntoTheWorld()
    {
        // A backpack that's merely being carried (not worn) still has permanent contents of its
        // own (see PlayerInventory's persistent-contents model) — dropping the bare token from a
        // hand must carry them along, not silently orphan them in the player's own
        // persistent-contents map.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        // The stipend's own debug_backpack already occupies "back" — clear it (and its
        // persistent contents, same reasoning as ResetTorsoAndHeadFromStipend above) so this
        // test's own fresh "backpack" equip has a free slot and a clean, empty container.
        player.Inventory.ClearEquippedContainer("back");
        player.Inventory.DiscardPersistentContents("debug_backpack");
        AssertBool(player.Inventory.EquipContainerDirectly("back", "backpack", new SlotContainer(2))).IsTrue();
        player.Inventory.Backpack!.Contents.Add("scrap_metal", 1);
        // Take it off into a hand (bare token, contents stay behind in persistent-contents).
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.TryUnequipBackpack();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "backpack")).IsTrue();

        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var handSlotIndex = player.Inventory.Hands.Slots[PlayerInventory.LeftHandSlotIndex]?.ItemId == "backpack"
            ? PlayerInventory.LeftHandSlotIndex
            : PlayerInventory.RightHandSlotIndex;
        var source = AutoFree(new InventorySlotUI { Container = player.Inventory.Hands, SlotIndex = handSlotIndex });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        AssertBool(player.Inventory.Hands.Slots[handSlotIndex] is null).IsTrue();
        AssertBool(player.Inventory.GetPersistentContents("backpack") is null).IsTrue();
        var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "backpack");
        AssertBool(dropped is not null).IsTrue();
        AssertBool(dropped!.Contents!.CountOf("scrap_metal") == 1).IsTrue();

        AutoFree(dropped);
    }
}
