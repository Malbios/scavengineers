using System.Collections.Generic;
using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Shop;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the shop's Buy/Sell entry-building and transaction methods.
/// All four depend on resolving the player via the "player" group (same lookup
/// ContainerPickupItem.GetPlayer already uses elsewhere), which needs a real PlayerScript
/// instance — Credits/Inventory are plain field-initialized state, unaffected by _Ready(), so a
/// minimal subclass that overrides _Ready() to skip the real one's dozens of HUD GetNode calls
/// (and its debug stipend) gives a clean, fully isolated Player without loading Player.tscn — this
/// test project (Scavengineers.NodeTests) is its own separate Godot project with no Scenes/ of
/// its own, so the real scene isn't even loadable here.
///
/// That same isolation means there's no res://Data/items.json here either, so the real
/// ItemCatalog.Load() would yield an empty catalog and every price would read 0 — the vendor now
/// reads prices from the catalog rather than its own hardcoded table. This suite therefore seeds a
/// minimal catalog of its own.
///
/// The seeded ids are deliberately synthetic (<c>test_*</c>) rather than real ones like
/// scrap_metal/wall_panel. ItemCatalog is process-wide static and gdUnit4 gives no guarantee that
/// this suite's seed is torn down before another suite runs, so seeding *real* ids leaks changed
/// MaxStackSize/EquipSlot values into unrelated suites — which is exactly what happened: it made
/// ShipBuildTargetLootTest and PlayerEquipSlotTest fail intermittently, depending on suite order.
/// With synthetic ids a leak is inert: every other suite queries real ids, which stay unknown to
/// the catalog and so keep resolving to precisely the same defaults as the empty catalog they see
/// today.
///
/// TestCheap is the "known-empty, not part of any stipend" item throughout, since the test player's
/// inventory starts genuinely empty.</summary>
[TestSuite]
public partial class VendorVerbTargetTest
{
    private const string TestCheap = "test_cheap";       // 10cr buy / 4cr sell
    private const string TestExpensive = "test_expensive"; // 40cr buy / 15cr sell
    private const string TestTradeable = "test_tradeable"; // 5cr buy / 2cr sell

    private partial class TestPlayer : PlayerScript
    {
        public override void _Ready()
        {
            AddToGroup("player");
        }
    }

    [Before]
    public void SeedCatalog() => ItemCatalog.SeedForTests(new List<ItemCatalog.ItemDefinition>
    {
        new() { Id = TestTradeable, MaxStackSize = 50, BuyPrice = 5, SellPrice = 2 },
        new() { Id = TestCheap, MaxStackSize = 20, BuyPrice = 10, SellPrice = 4 },
        new() { Id = TestExpensive, MaxStackSize = 1, BuyPrice = 40, SellPrice = 15 },
    });

    // Deliberately NO [After] ItemCatalog.ResetForTests(). Resetting sets the catalog back to null,
    // so the *next* suite to touch it re-runs Load() — which in this project-less test process
    // finds no res://Data/items.json, emits another GD.PushWarning, and generally reintroduces
    // exactly the kind of cross-suite timing variation this suite already caused once. Leaving the
    // seed in place means Load() runs at most once per process, as it did before this suite existed.
    // Safe precisely because the seeded ids are synthetic: no other suite queries them, and every
    // real id stays unknown to the catalog, resolving to the same defaults an empty one gives.

    private static (VendorVerbTarget Vendor, PlayerScript Player) CreateVendorWithPlayer(SceneTree sceneTree)
    {
        var player = AutoFree(new TestPlayer());
        sceneTree.Root.AddChild(player);

        var vendor = AutoFree(new VendorVerbTarget());
        sceneTree.Root.AddChild(vendor);

        return (vendor, player);
    }

    private static ShopEntry FindEntry(IReadOnlyList<ShopEntry> entries, string itemId) => entries.First(e => e.ItemId == itemId);

    [TestCase]
    [RequireGodotRuntime]
    public void BuildBuyEntries_DisabledWhenUnaffordable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        // 40cr — well above the starting stipend.
        var expensive = FindEntry(vendor.BuildBuyEntries(), TestExpensive);

        AssertBool(expensive.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildBuyEntries_EnabledWhenAffordableWithRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        var cheap = FindEntry(vendor.BuildBuyEntries(), TestCheap);

        AssertBool(cheap.Disabled).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildSellEntries_DisabledWhenNotOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        var cheap = FindEntry(vendor.BuildSellEntries(), TestCheap);

        AssertBool(cheap.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildSellEntries_EnabledWhenOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.Inventory.Add(TestTradeable, 1);

        var tradeable = FindEntry(vendor.BuildSellEntries(), TestTradeable);

        AssertBool(tradeable.Disabled).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryBuy_SpendsCreditsAndAddsItem()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        var creditsBefore = player.Credits;

        var bought = vendor.TryBuy(TestCheap);

        AssertBool(bought).IsTrue();
        AssertBool(player.Credits == creditsBefore - 10).IsTrue();
        AssertBool(player.Inventory.Has(TestCheap, 1)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryBuy_FailsCleanlyWhenUnaffordable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.TrySpendCredits(player.Credits); // drain to 0

        var bought = vendor.TryBuy(TestCheap);

        AssertBool(bought).IsFalse();
        AssertBool(player.Credits == 0).IsTrue();
        AssertBool(player.Inventory.Has(TestCheap, 1)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TrySell_GrantsCreditsAndRemovesItem()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.Inventory.Add(TestTradeable, 1);
        var creditsBefore = player.Credits;

        var sold = vendor.TrySell(TestTradeable);

        AssertBool(sold).IsTrue();
        AssertBool(player.Credits == creditsBefore + 2).IsTrue();
        AssertBool(player.Inventory.Has(TestTradeable, 1)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TrySell_FailsCleanlyWhenNotOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        var creditsBefore = player.Credits;

        var sold = vendor.TrySell(TestCheap);

        AssertBool(sold).IsFalse();
        AssertBool(player.Credits == creditsBefore).IsTrue();
    }

    /// <summary>The regression this whole data-driven move exists for: the shop used to index a
    /// hardcoded price dict with ids from a separate hardcoded list, so an id missing from the dict
    /// threw. Nothing is tradeable-but-priceless now (the item set *is* derived from the prices),
    /// but an untradeable id reaching TryBuy/TrySell by another route must still refuse cleanly
    /// rather than transacting for 0 credits.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public void TryBuyAndTrySell_RefuseAnUntradeableItem_WithoutThrowingOrTransacting()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.Inventory.Add("not_in_the_catalog_at_all", 1);
        var creditsBefore = player.Credits;

        AssertBool(vendor.TryBuy("not_in_the_catalog_at_all")).IsFalse();
        AssertBool(vendor.TrySell("not_in_the_catalog_at_all")).IsFalse();

        AssertBool(player.Credits == creditsBefore).IsTrue();
        AssertBool(player.Inventory.Has("not_in_the_catalog_at_all", 1)).IsTrue();
    }
}
