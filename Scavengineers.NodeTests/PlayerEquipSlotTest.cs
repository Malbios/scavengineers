using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the generalized Torso/Head equip-slot flow
/// (Player.TryEquipItemFrom/TryUnequipItem) and the Legs/LeftFoot/RightFoot "blocked while the EVA
/// suit's torso is worn" gate. Equips directly via PlayerInventory.EquipContainerDirectly rather
/// than through ItemCatalog.EquipSlot — this project's isolated NodeTests catalog has no real
/// Data/items.json, so that gate always returns null here regardless of item id; it's covered
/// instead by ItemCatalogTests.EquipSlot_ReturnsTheDeclaredSlot plus manual playtest.</summary>
[TestSuite]
public class PlayerEquipSlotTest
{
    /// <summary>The harness's fresh-game stipend already wears a starter EVA suit — undo that,
    /// including its persistent-contents entries (which survive unwearing), so a test's own
    /// from-scratch equip doesn't silently inherit the stipend's leftover container.</summary>
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
        // The stipend's scrap_metal overflows the debug backpack into both hands here (catalog-
        // less MaxStackSize falls back to 1) — clear them so "room exists" is a real precondition.
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
        // Contents never travel with the unequip decision, so both hands full with no drop
        // destination just means the suit stays worn — same "nothing vanishes" contract as empty.
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
        // Simulates TryEquipItemFrom's own torso-specific glue — exercised directly since that
        // path needs a real ItemCatalog.EquipSlot match, unreachable here (see class doc).
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
        // FitsInStorage's suit-specific restriction is covered by ItemCatalogTests instead (see
        // class doc) — the helmet fits everywhere regardless, so this is still a valid
        // destination-respecting case.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.EquipContainerDirectly("head", "eva_helmet", new SlotContainer(0));

        // A standalone container, not the stipend-filled real backpack, isolates this test's
        // "is this exact slot empty" precondition from stipend noise.
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
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("widget", 1, 1f));
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));
        player.Inventory.AttachSpecializedSlot("suit_o2", hasItem: true, charge: 0.6f);

        player.TryUnequipItem("torso");

        // The suit never actually left, so its sub-slots must be exactly as they were.
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
    public void CanDropData_StillAcceptsAnOrdinaryCompatibleItem_LikeTheHelmet_OntoABackpackSlot()
    {
        // The helmet has no storage restriction, so this stays accepted regardless of ItemCatalog
        // state — unlike the suit-specific rejection case, covered by ItemCatalogTests instead.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.EquipContainerDirectly("head", "eva_helmet", new SlotContainer(0));

        var source = AutoFree(new InventorySlotUI { EquippedSlotName = "head", PlayerRef = player });
        var destination = AutoFree(new InventorySlotUI { Container = new SlotContainer(2), PlayerRef = player });

        AssertBool(destination!._CanDropData(Vector2.Zero, Variant.From(source))).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void CanDropData_ChecksTheSourcesCurrentItem_NotJustAnOrdinaryContainerSlot()
    {
        // Dragging a *worn* item (EquippedSlotName-sourced, no ordinary Container/SlotIndex) must
        // resolve via source.CurrentSlot(), not silently skip the check.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));

        var source = AutoFree(new InventorySlotUI { EquippedSlotName = "torso", PlayerRef = player });
        var destination = AutoFree(new InventorySlotUI { Container = new SlotContainer(2), PlayerRef = player });

        // Just needs to resolve without throwing — the true/false value reflects this
        // environment's catalog-less FitsInStorage default, not the real game's restriction.
        destination!._CanDropData(Vector2.Zero, Variant.From(source));
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task UnusedBodySlot_NeverDisplaysAnItem_EvenWhenTheSharedHandsContainerHasSomethingAtIndexZero()
    {
        // Regression test: Legs/LeftFoot/RightFoot never set their own SlotIndex (defaults to 0),
        // and CurrentSlot() used to have no IsUnusedBodySlot guard, so it fell through to reading
        // Hands.Slots[0] directly — showing the left hand's item on a slot that should stay empty.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, ("widget", 1, 1f));

        var legSlot = AutoFree(new InventorySlotUI { IsUnusedBodySlot = true, PlayerRef = player, Container = player.Inventory.Hands });
        legSlot!.AddChild(new ColorRect { Name = "Icon" });
        legSlot.AddChild(new Label { Name = "Count" });
        sceneTree.Root.AddChild(legSlot);

        // Waits on the slot's own idle _Process (Refresh), not on Player — legSlot is standalone
        // here, so Player's physics tick says nothing about whether it has refreshed yet.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

        var icon = legSlot.GetNode<ColorRect>("Icon");
        AssertBool(icon.Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryDropInWorld_EquippedTorsoSlot_SpawnsTheSuitAsAWorldPickup_NotWhateverIsInTheLeftHand()
    {
        // Regression test: TryDropInWorld had no EquippedSlotName check, so dragging a worn
        // Torso/Head item fell through to the generic Container/SlotIndex path (Hands/0) and
        // would silently drop/clear whatever was in the left hand instead.
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
    public void TryDropInWorld_AimedAtAVerticalWall_NudgesAlongTheWallsNormal_NotStraightUp()
    {
        // Regression test: RestingDropPosition used to always nudge straight up regardless of the
        // raycast's hit normal, embedding items dropped against a wall instead of resting them
        // nearby. Aiming dead-center at this wall hits with a horizontal normal (~(0,0,1)) — the
        // fix nudges along THAT normal, so the dropped item's Y should stay at 0, not boosted up.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));

        var wall = AutoFree(new StaticBody3D { Position = new Vector3(0, 0, -1.5f) });
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        sceneTree.Root.AddChild(wall);

        var source = AutoFree(new InventorySlotUI { EquippedSlotName = "torso" });
        var viewportCenter = player.GetViewport().GetVisibleRect().GetCenter();

        player.TryDropInWorld(source, viewportCenter);

        var dropped = sceneTree.Root.GetChildren().OfType<ContainerPickupItem>().FirstOrDefault(c => c.ItemId == "eva_torso_suit");
        AssertBool(dropped is not null).IsTrue();
        AssertFloat(dropped!.Position.Y).IsEqual(0f);

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
        // A carried-not-worn backpack still has persistent contents of its own — dropping the
        // bare token from a hand must carry them along, not orphan them in the player's map.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        // The stipend's debug_backpack already occupies "back" — clear it and its persistent
        // contents so this test's fresh "backpack" equip gets a clean container.
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

    [TestCase]
    [RequireGodotRuntime]
    public async Task ToggleItemWindow_OpensTheSuitWindowAndPointsItsPocketsAtTheRightContents_WhileTheSuitIsMerelyHeldNotWorn()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);
        player.Inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(2));
        player.Inventory.Torso!.Contents.Add("scrap_metal", 1);
        // Bypasses the full unequip flow (contents persist regardless) so this test only
        // exercises the preview mechanic itself.
        player.Inventory.ClearEquippedContainer("torso");
        AssertBool(player.Inventory.Torso is null).IsTrue();

        // A Control's raw Godot default is Visible, so let one frame of UpdateInventoryHud run
        // first to establish the real closed-by-default baseline before checking it.
        var suitWindow = player.GetNode<Control>("HUD/SuitWindow");
        await FrameWait.UntilPlayerProcessedAsync(sceneTree, player);
        AssertBool(suitWindow.Visible).IsFalse();

        player.ToggleItemWindow("eva_torso_suit");

        AssertBool(suitWindow.Visible).IsTrue();

        // Two-step await (matches this project's established convention) so UpdateInventoryHud's
        // per-frame Container re-point has actually run by the time we check it.
        await FrameWait.UntilPlayerProcessedAsync(sceneTree, player);

        var pocket1 = player.GetNode<InventorySlotUI>("HUD/SuitWindow/Layout/SuitGrid/Pocket1");
        AssertBool(ReferenceEquals(pocket1.Container, player.Inventory.GetPersistentContents("eva_torso_suit"))).IsTrue();
        AssertBool(pocket1.Container!.CountOf("scrap_metal") == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ToggleItemWindow_DoesNothing_ForAnItemWithNoPersistentContentsAnywhere()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        ResetTorsoAndHeadFromStipend(player.Inventory);

        var suitWindow = player.GetNode<Control>("HUD/SuitWindow");
        await FrameWait.UntilPlayerProcessedAsync(sceneTree, player);
        AssertBool(suitWindow.Visible).IsFalse();

        player.ToggleItemWindow("eva_torso_suit");

        AssertBool(suitWindow.Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_Pda_ReturnsItToAHand_WhenRoomExists()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("pda", "pda", new SlotContainer(1));

        player.TryUnequipItem("pda");

        AssertBool(player.Inventory.GetEquippedContainer("pda") is null).IsTrue();
        AssertBool(player.Inventory.Hands.Slots.Any(s => s?.ItemId == "pda")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryUnequipItem_Pda_LeavesItsCartridgePersistentContentsIntact_WhileMerelyHeld()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.Hands.SetSlot(PlayerInventory.LeftHandSlotIndex, null);
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, null);
        player.Inventory.EquipContainerDirectly("pda", "pda", new SlotContainer(1));
        player.Inventory.GetEquippedContainer("pda")!.Contents.Add("health_scan_cartridge", 1);

        player.TryUnequipItem("pda");

        AssertBool(player.Inventory.GetEquippedContainer("pda") is null).IsTrue();
        var contents = player.Inventory.GetPersistentContents("pda");
        AssertBool(contents is not null).IsTrue();
        AssertBool(contents!.CountOf("health_scan_cartridge") == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ToggleItemWindow_OpensThePdaWindowAndPointsItsCartridgeSlotAtTheRightContents()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.EquipContainerDirectly("pda", "pda", new SlotContainer(1));
        player.Inventory.GetEquippedContainer("pda")!.Contents.Add("health_scan_cartridge", 1);

        var pdaWindow = player.GetNode<Control>("HUD/PdaWindow");
        await FrameWait.UntilPlayerProcessedAsync(sceneTree, player);
        AssertBool(pdaWindow.Visible).IsFalse();

        player.ToggleItemWindow("pda");

        AssertBool(pdaWindow.Visible).IsTrue();

        await FrameWait.UntilPlayerProcessedAsync(sceneTree, player);

        var cartridgeSlot = player.GetNode<InventorySlotUI>("HUD/PdaWindow/Layout/PdaGrid/Cartridge1");
        AssertBool(ReferenceEquals(cartridgeSlot.Container, player.Inventory.GetPersistentContents("pda"))).IsTrue();
        AssertBool(cartridgeSlot.Container!.CountOf("health_scan_cartridge") == 1).IsTrue();

        // The second cartridge pocket (power_scan_cartridge) must be wired up the exact same
        // way — a real bug this test would have caught: it was left permanently un-bound
        // (Container always null), silently rejecting any drag-and-drop into it.
        var cartridgeSlot2 = player.GetNode<InventorySlotUI>("HUD/PdaWindow/Layout/PdaGrid/Cartridge2");
        AssertBool(ReferenceEquals(cartridgeSlot2.Container, player.Inventory.GetPersistentContents("pda"))).IsTrue();
    }
}
