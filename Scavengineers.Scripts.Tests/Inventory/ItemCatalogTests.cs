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
}
