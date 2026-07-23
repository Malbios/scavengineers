using System.Text.Json;

using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Tests.Inventory;

[Collection(ItemCatalogCollection.Name)]
public class ItemCatalogTests : IDisposable
{
    public ItemCatalogTests()
    {
        ItemCatalog.SeedForTests(new Dictionary<string, ItemCatalog.ItemDefinition>
        {
            ["flashlight"] = new() { Id = "flashlight", MaxStackSize = 1, IsToggleableLight = true },
            ["debug_flashlight"] = new() { Id = "debug_flashlight", MaxStackSize = 1, IsToggleableLight = true },
            ["widget"] = new() { Id = "widget", MaxStackSize = 1 },
            ["eva_torso_suit"] = new() { Id = "eva_torso_suit", MaxStackSize = 1, EquipSlot = "torso", FitsInStorage = false },
            ["eva_helmet"] = new() { Id = "eva_helmet", MaxStackSize = 1, EquipSlot = "head" },
            ["backpack"] = new() { Id = "backpack", MaxStackSize = 1 },
            ["pda"] = new() { Id = "pda", MaxStackSize = 1, EquipSlot = "pda" },
            ["small_bin"] = new() { Id = "small_bin", MaxStackSize = 5, StorageSlotCount = 2, BuyPrice = 15, SellPrice = 6 },
            ["shelf"] = new() { Id = "shelf", MaxStackSize = 5, StorageSlotCount = 6, BuyPrice = 30, SellPrice = 12 },
            ["large_shelf"] = new() { Id = "large_shelf", MaxStackSize = 5, StorageSlotCount = 10, BuyPrice = 50, SellPrice = 20 },
        });
    }

    public void Dispose() => ItemCatalog.ResetForTests();

    [Theory]
    [InlineData("flashlight", true)]
    [InlineData("debug_flashlight", true)]
    [InlineData("widget", false)]
    [InlineData("unknown_item", false)]
    public void IsToggleableLight_ReturnsTrueOnlyForFlashlightAndDebugFlashlight_FalseForEverythingElse(string itemId, bool expected)
    {
        Assert.Equal(expected, ItemCatalog.IsToggleableLight(itemId));
    }

    [Theory]
    [InlineData("eva_torso_suit", "torso")]
    [InlineData("eva_helmet", "head")]
    [InlineData("pda", "pda")]
    [InlineData("widget", null)]
    [InlineData("unknown_item", null)]
    public void EquipSlot_ReturnsTheDeclaredSlot_NullForAnythingNotEquippableThatWay(string itemId, string? expected)
    {
        Assert.Equal(expected, ItemCatalog.EquipSlot(itemId));
    }

    [Theory]
    [InlineData("eva_torso_suit", false)]
    [InlineData("eva_helmet", true)]
    [InlineData("backpack", true)]
    [InlineData("widget", true)]
    [InlineData("unknown_item", true)]
    public void FitsInStorage_IsFalseOnlyForTheEvaTorsoSuit_TrueForEverythingElseIncludingUnknownIds(string itemId, bool expected)
    {
        Assert.Equal(expected, ItemCatalog.FitsInStorage(itemId));
    }

    [Theory]
    [InlineData("small_bin", 2)]
    [InlineData("shelf", 6)]
    [InlineData("large_shelf", 10)]
    [InlineData("widget", 0)]
    [InlineData("unknown_item", 0)]
    public void StorageSlotCount_ReturnsTheDeclaredCount_ZeroForAnythingNotInstallableAsStorage(string itemId, int expected)
    {
        Assert.Equal(expected, ItemCatalog.StorageSlotCount(itemId));
    }

    [Fact]
    public void StorageItemIds_ContainsExactlyEveryItemWithANonzeroStorageSlotCount()
    {
        Assert.Equal(
            new HashSet<string> { "small_bin", "shelf", "large_shelf" },
            ItemCatalog.StorageItemIds.ToHashSet());
    }

    [Theory]
    [InlineData("small_bin", 15, 6)]
    [InlineData("shelf", 30, 12)]
    [InlineData("large_shelf", 50, 20)]
    [InlineData("widget", 0, 0)]
    [InlineData("unknown_item", 0, 0)]
    public void BuyAndSellPrice_ReturnTheDeclaredPrices_ZeroForAnythingUntradeable(string itemId, int expectedBuy, int expectedSell)
    {
        Assert.Equal(expectedBuy, ItemCatalog.BuyPrice(itemId));
        Assert.Equal(expectedSell, ItemCatalog.SellPrice(itemId));
    }

    [Fact]
    public void TradeableItemIds_ContainsExactlyEveryItemWithANonzeroBuyPrice()
    {
        Assert.Equal(
            new HashSet<string> { "small_bin", "shelf", "large_shelf" },
            ItemCatalog.TradeableItemIds.ToHashSet());
    }
}

/// <summary>Guards the real Data/items.json rather than a seeded stand-in — the shop's item list
/// and its prices are now one source (see ItemCatalog.TradeableItemIds), so the failure this
/// replaces (an item listed for sale with no price, throwing KeyNotFoundException the moment the
/// shop panel opened) is only possible again if a real catalog entry declares one price without
/// the other. Reads the file directly since ItemCatalog.Load() needs a running Godot engine.</summary>
public class ItemsJsonTradePriceTests
{
    private static readonly string ItemsJsonPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Data", "items.json");

    [Fact]
    public void EveryItemDeclaringOneTradePriceDeclaresBoth_AndBuyAlwaysExceedsSell()
    {
        var definitions = JsonSerializer.Deserialize<List<ItemCatalog.ItemDefinition>>(File.ReadAllText(ItemsJsonPath));

        Assert.NotNull(definitions);
        Assert.NotEmpty(definitions);

        foreach (var item in definitions)
        {
            if (item.BuyPrice == 0 && item.SellPrice == 0)
            {
                continue; // deliberately untradeable (tools, debug items, quest items)
            }

            Assert.True(item.BuyPrice > 0, $"'{item.Id}' has a sell price but no buy price — it would be listed unbuyable.");
            Assert.True(item.SellPrice > 0, $"'{item.Id}' has a buy price but no sell price — it would sell back for nothing.");

            // Buy above Sell is what stops a trivial buy-then-sell arbitrage loop.
            Assert.True(item.BuyPrice > item.SellPrice, $"'{item.Id}' sells for at least what it costs — free credits.");
        }
    }
}
