using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Tests.Ship;

[Collection(ShipLayoutCatalogCollection.Name)]
public class ShipLayoutCatalogTests : IDisposable
{
    public ShipLayoutCatalogTests()
    {
        ShipLayoutCatalog.SeedForTests(new Dictionary<string, ShipLayoutCatalog.ShipLayoutDefinition>
        {
            ["derelict_small"] = new()
            {
                Id = "derelict_small",
                GridWidth = 12,
                RoomSplitColumns = [6],
                HasHullBreaches = true,
                HasFireHazard = true,
                InitialBreaches = [new() { CellX = 9, CellY = 5, OutsideX = 9, OutsideY = 6 }],
                FireGeneratorCell = new() { X = 1, Y = 4 },
                DamagedConduitCell = new() { X = 1, Y = 3 },
            },
        });
    }

    public void Dispose() => ShipLayoutCatalog.ResetForTests();

    [Fact]
    public void TryGet_ReturnsTheSeededDefinition_ForAKnownId()
    {
        var layout = ShipLayoutCatalog.TryGet("derelict_small");

        Assert.NotNull(layout);
        Assert.Equal(12, layout!.GridWidth);
        Assert.Equal([6], layout.RoomSplitColumns);
        Assert.Single(layout.InitialBreaches);
    }

    [Fact]
    public void TryGet_ReturnsNull_ForAnUnknownId()
    {
        Assert.Null(ShipLayoutCatalog.TryGet("no_such_layout"));
    }
}
