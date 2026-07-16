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
            ["eva_torso_suit"] = new() { Id = "eva_torso_suit", MaxStackSize = 1, EquipSlot = "torso" },
            ["eva_helmet"] = new() { Id = "eva_helmet", MaxStackSize = 1, EquipSlot = "head" },
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
    [InlineData("widget", null)]
    [InlineData("unknown_item", null)]
    public void EquipSlot_ReturnsTheDeclaredSlot_NullForAnythingNotEquippableThatWay(string itemId, string? expected)
    {
        Assert.Equal(expected, ItemCatalog.EquipSlot(itemId));
    }
}
