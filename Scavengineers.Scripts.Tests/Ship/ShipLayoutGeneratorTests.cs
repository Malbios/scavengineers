using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Tests.Ship;

public class ShipLayoutGeneratorTests
{
    private const int SeedSweepCount = 200;

    private static readonly string[] KnownItemIds = ["scrap_metal", "wall_panel", "spare_parts", "power_cell"];

    [Fact]
    public void Generate_IsFullyDeterministic_ForTheSameSeed()
    {
        var a = ShipLayoutGenerator.Generate(42);
        var b = ShipLayoutGenerator.Generate(42);

        Assert.Equal(a.Layout.GridWidth, b.Layout.GridWidth);
        Assert.Equal(a.Layout.RoomSplitColumns, b.Layout.RoomSplitColumns);
        Assert.Equal(a.Layout.HasFireHazard, b.Layout.HasFireHazard);
        Assert.Equal(a.Loot.Count, b.Loot.Count);
        for (var i = 0; i < a.Loot.Count; i++)
        {
            Assert.Equal(a.Loot[i], b.Loot[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generate_RoomSplitColumnsFirstEntry_IsAlwaysColumn6(int seed)
    {
        var generated = ShipLayoutGenerator.Generate(seed);

        Assert.Equal(6, generated.Layout.RoomSplitColumns[0]);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generate_GridWidthAndEveryRoomWidth_StayWithinSaneBounds(int seed)
    {
        var generated = ShipLayoutGenerator.Generate(seed);
        var layout = generated.Layout;

        Assert.InRange(layout.GridWidth, 10, 30);

        var boundaries = new List<int> { 0 };
        boundaries.AddRange(layout.RoomSplitColumns);
        boundaries.Add(layout.GridWidth);

        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            Assert.True(boundaries[i + 1] - boundaries[i] >= 4, $"seed {seed}: room {i} narrower than 4 tiles");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generate_EveryBreach_IsAGenuineBoundaryEdge_NeverARoomSplitEdge(int seed)
    {
        var generated = ShipLayoutGenerator.Generate(seed);
        var layout = generated.Layout;
        int[] doorwayRows = [2, 3];

        foreach (var breach in layout.InitialBreaches)
        {
            // Each of the 4 categories below uses an "outside" sentinel (-1, GridWidth,
            // GridDepth) that only ever appears on a genuine boundary edge -- a RoomSplitColumns
            // edge's "outside" is always a real adjacent room cell, so it can never match any of
            // these, making this check sufficient on its own (no separate split-edge exclusion
            // needed).
            var isNorth = breach.CellY == 0 && breach.OutsideY == -1;
            var isSouth = breach.CellY == 5 && breach.OutsideY == 6;
            var isWest = breach.CellX == 0 && breach.OutsideX == -1 && !doorwayRows.Contains(breach.CellY);
            var isEast = breach.CellX == layout.GridWidth - 1 && breach.OutsideX == layout.GridWidth && !doorwayRows.Contains(breach.CellY);

            Assert.True(isNorth || isSouth || isWest || isEast, $"seed {seed}: breach ({breach.CellX},{breach.CellY})->({breach.OutsideX},{breach.OutsideY}) isn't a genuine boundary edge");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generate_WhenFireHazardIsOn_CellsAreManhattanAdjacent_AndNeverShareARoomWithABreach(int seed)
    {
        var generated = ShipLayoutGenerator.Generate(seed);
        var layout = generated.Layout;

        if (!layout.HasFireHazard)
        {
            return;
        }

        var fire = layout.FireGeneratorCell!;
        var conduit = layout.DamagedConduitCell!;
        var manhattan = Math.Abs(fire.X - conduit.X) + Math.Abs(fire.Y - conduit.Y);
        Assert.Equal(1, manhattan);

        var hazardInRoom1 = conduit.X < 6;
        foreach (var breach in layout.InitialBreaches)
        {
            var breachInRoom1 = breach.CellX < 6;
            Assert.NotEqual(hazardInRoom1, breachInRoom1);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generate_EveryLootTile_IsInBoundsAndNotOnOrAdjacentToAHazard(int seed)
    {
        var generated = ShipLayoutGenerator.Generate(seed);
        var layout = generated.Layout;

        var excluded = new HashSet<(int, int)>();
        foreach (var breach in layout.InitialBreaches)
        {
            excluded.Add((breach.CellX, breach.CellY));
            excluded.Add((breach.CellX + 1, breach.CellY));
            excluded.Add((breach.CellX - 1, breach.CellY));
            excluded.Add((breach.CellX, breach.CellY + 1));
            excluded.Add((breach.CellX, breach.CellY - 1));
        }

        if (layout.HasFireHazard)
        {
            var fire = layout.FireGeneratorCell!;
            var conduit = layout.DamagedConduitCell!;
            excluded.Add((fire.X, fire.Y));
            excluded.Add((conduit.X, conduit.Y));
        }

        foreach (var loot in generated.Loot)
        {
            Assert.InRange(loot.TileX, 0, layout.GridWidth - 1);
            Assert.InRange(loot.TileY, 0, 5);
            Assert.Contains(loot.ItemId, KnownItemIds);
            Assert.True(loot.Count >= 1);
            Assert.False(excluded.Contains((loot.TileX, loot.TileY)), $"seed {seed}: loot landed on/adjacent to a hazard cell at ({loot.TileX},{loot.TileY})");
        }
    }

    public static IEnumerable<object[]> Seeds()
    {
        for (var seed = 0; seed < SeedSweepCount; seed++)
        {
            yield return new object[] { seed };
        }
    }
}
