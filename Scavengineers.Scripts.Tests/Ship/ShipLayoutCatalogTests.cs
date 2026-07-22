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
            ["derelict_1"] = new()
            {
                Id = "derelict_1",
                GridWidth = 18,
                RoomSplitColumns = [6, 12],
                LadderCell = new() { X = 2, Y = 2 },
                SecondDeck = new()
                {
                    Id = "derelict_1_deck2",
                    GridWidth = 6,
                    HasHullBreaches = true,
                    InitialBreaches = [new() { CellX = 4, CellY = 5, OutsideX = 4, OutsideY = 6 }],
                },
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

    [Fact]
    public void SecondDeck_IsNull_ForALayoutThatNeverSetIt()
    {
        // Backward compatibility: every layout predating this field (and any single-deck ship
        // going forward) must keep deserializing/round-tripping to null, not some default.
        var layout = ShipLayoutCatalog.TryGet("derelict_small");

        Assert.NotNull(layout);
        Assert.Null(layout!.SecondDeck);
        Assert.Null(layout.LadderCell);
    }

    [Fact]
    public void SecondDeck_RoundTripsTheNestedDefinition_AndItsOwnIndependentHazard()
    {
        var layout = ShipLayoutCatalog.TryGet("derelict_1");

        Assert.NotNull(layout);
        Assert.NotNull(layout!.LadderCell);
        Assert.Equal(2, layout.LadderCell!.X);
        Assert.Equal(2, layout.LadderCell.Y);

        var secondDeck = layout.SecondDeck;
        Assert.NotNull(secondDeck);
        Assert.Equal("derelict_1_deck2", secondDeck!.Id);
        Assert.Equal(6, secondDeck.GridWidth);
        Assert.True(secondDeck.HasHullBreaches);
        Assert.Single(secondDeck.InitialBreaches);
        Assert.Null(secondDeck.SecondDeck); // depth capped at 1
    }
}
