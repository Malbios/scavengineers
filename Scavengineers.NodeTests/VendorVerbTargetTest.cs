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

/// <summary>Regression coverage for the shop's Buy/Sell entry-building and transaction methods,
/// via a minimal PlayerScript subclass that skips the real _Ready()'s HUD/stipend setup (this
/// project can't load Player.tscn at all) and a minimal seeded ItemCatalog (no real
/// Data/items.json here either).
///
/// The seeded ids are deliberately synthetic (<c>test_*</c>): ItemCatalog is process-wide static
/// with no guaranteed teardown between suites, and seeding real ids once leaked changed
/// MaxStackSize/EquipSlot values into ShipBuildTargetLootTest/PlayerEquipSlotTest, causing
/// intermittent failures depending on suite order. Synthetic ids make a leak inert since no other
/// suite queries them.</summary>
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

    // Deliberately NO [After] ItemCatalog.ResetForTests() — resetting would make the next suite
    // re-run Load() and reintroduce the cross-suite timing variation this suite exists to avoid.
    // Safe because the seeded ids are synthetic, so no other suite queries them.

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
