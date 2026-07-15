using System;
using System.Collections.Generic;
using System.Linq;

namespace Scavengineers.Scripts.Ship;

/// <summary>A single procedurally-placed pickup — see ShipBuildTarget.SpawnGeneratedLoot, which
/// turns this into a real world PickupItem via the existing InventoryOverflow.DropAt helper.</summary>
public readonly record struct LootSpawn(string ItemId, int Count, int TileX, int TileY);

/// <summary>The generator's own output shape — kept separate from ShipLayoutCatalog.ShipLayoutDefinition
/// (the JSON-loaded schema) so the catalog's on-disk format doesn't have to carry generator-only
/// loot data.</summary>
public sealed class GeneratedShipLayout
{
    public required ShipLayoutCatalog.ShipLayoutDefinition Layout { get; init; }

    public required IReadOnlyList<LootSpawn> Loot { get; init; }
}

/// <summary>
/// Produces a random, always-valid derelict layout from a single integer seed — deterministic
/// (same seed -> byte-identical output), so a persisted seed (see ShipSim.ProcedurallyGenerate)
/// fully reproduces a derelict's shape across a save/reload without needing to persist the shape
/// itself. Deliberately uses plain System.Random rather than Godot's RandomNumberGenerator: this
/// project already hit a real crash (GD.PushWarning failing outside a running engine, this same
/// session) from a Godot-native call executed without the engine initialized —
/// RandomNumberGenerator is a full RefCounted-derived Godot class with the same category of risk,
/// not a plain struct. System.Random is pure BCL, has zero engine dependency, and is exactly as
/// deterministic for pure data generation — this sidesteps that risk rather than gambling on it,
/// and keeps this class (and its tests) fully engine-free.
///
/// Every generated layout respects the hard constraints this codebase's own scene geometry
/// imposes (see ShipSim.cs/ShipBuildTarget.cs/InteriorDoorVerbTarget.cs for the originals):
/// - RoomSplitColumns[0] is always 6 — the only split with a real door object
///   (InteriorDoorVerbTarget's edges and ShipBuildTarget.ExcludedEdgeColumn are both hand-baked
///   to column 6 and never read a layout's own split columns).
/// - WestCorridorLength/EastCorridorLength are never part of this output at all — the shared
///   DerelictAirlock's own fixed docking tile assumes exactly today's scene-authored corridor
///   length; this generator only ever varies the room-side of the ship.
/// - Every hull breach lands on a genuine boundary edge (never a RoomSplitColumns edge, whose
///   "outside" is always a real adjacent room — breaching one would silently vent two rooms into
///   one connected vacuum with no visible hole, see Deck.IsHullBreached).
/// - A fresh ship has exactly two connected components at world-start: Room 1 (InteriorDoorVerbTarget
///   starts closed) and everything else (every other split has no door object, its doorway rows
///   stay permanently open) — the fire hazard, if generated, never shares a component with a
///   breach, or it could never ignite (FireSystem only ignites once O2Fraction >= 0.1, and a
///   breached room vents to vacuum immediately).
/// </summary>
public static class ShipLayoutGenerator
{
    // Must mirror ShipSim.GridDepth/DoorwayRows exactly — this generator targets the exact same
    // fixed grid depth/doorway convention every ship in this game already shares.
    private const int GridDepth = 6;
    private static readonly int[] DoorwayRows = [2, 3];

    // Placeholder/tunable — every range below is a first-pass balance guess, not a load-bearing
    // design constraint. 1-3 additional rooms (2-4 total, matching the two hand-authored
    // precedents: derelict_small has 2, derelict_1 has 3).
    private const int MinAdditionalRooms = 1;
    private const int MaxAdditionalRoomsExclusive = 4;
    private const int MinRoomWidth = 4;
    private const int MaxRoomWidthExclusive = 9;
    private const int MinBreaches = 1;
    private const int MaxBreachesExclusive = 4;
    private const double FireHazardChance = 0.6;
    private const int MinLootCount = 4;
    private const int MaxLootCountExclusive = 8;

    private static readonly (string ItemId, int Weight, int MinCount, int MaxCountExclusive)[] LootTable =
    [
        ("scrap_metal", 4, 1, 4),
        ("wall_panel", 3, 1, 3),
        ("spare_parts", 2, 1, 3),
        ("power_cell", 1, 1, 2),
    ];

    public static GeneratedShipLayout Generate(int seed)
    {
        var rng = new Random(seed);

        var (gridWidth, roomSplitColumns) = GenerateRooms(rng);
        var (hasFireHazard, fireGeneratorCell, damagedConduitCell, fireHazardIsInRoom1) = GenerateFireHazard(rng, gridWidth);
        var breaches = GenerateBreaches(rng, gridWidth, hasFireHazard, fireHazardIsInRoom1);
        var loot = GenerateLoot(rng, gridWidth, breaches, hasFireHazard, fireGeneratorCell, damagedConduitCell);

        var layout = new ShipLayoutCatalog.ShipLayoutDefinition
        {
            Id = $"generated_{seed}",
            GridWidth = gridWidth,
            RoomSplitColumns = roomSplitColumns,
            EastCorridorLength = 0,
            HasHullBreaches = true,
            HasFireHazard = hasFireHazard,
            InitialBreaches = breaches,
            FireGeneratorCell = hasFireHazard
                ? new ShipLayoutCatalog.CellPosition { X = fireGeneratorCell.X, Y = fireGeneratorCell.Y }
                : null,
            DamagedConduitCell = hasFireHazard
                ? new ShipLayoutCatalog.CellPosition { X = damagedConduitCell.X, Y = damagedConduitCell.Y }
                : null,
        };

        return new GeneratedShipLayout { Layout = layout, Loot = loot };
    }

    /// <summary>Room 1 is always columns [0,6) — non-negotiable (see class doc comment). Each
    /// additional room's start column is recorded as a split before its own width is rolled and
    /// added to the running grid width, so the final room never gets its own trailing split
    /// (matching derelict_1's real [6,12] for GridWidth=18: 3 rooms, 2 splits).</summary>
    private static (int GridWidth, int[] RoomSplitColumns) GenerateRooms(Random rng)
    {
        var additionalRooms = rng.Next(MinAdditionalRooms, MaxAdditionalRoomsExclusive);
        var gridWidth = 6;
        var roomSplitColumns = new List<int>();

        for (var i = 0; i < additionalRooms; i++)
        {
            roomSplitColumns.Add(gridWidth);
            gridWidth += rng.Next(MinRoomWidth, MaxRoomWidthExclusive);
        }

        return (gridWidth, roomSplitColumns.ToArray());
    }

    private static bool IsInRoom1(int cellX) => cellX < 6;

    private static (bool HasFireHazard, (int X, int Y) FireGeneratorCell, (int X, int Y) DamagedConduitCell, bool IsInRoom1) GenerateFireHazard(Random rng, int gridWidth)
    {
        if (rng.NextDouble() >= FireHazardChance)
        {
            return (false, default, default, false);
        }

        var hostInRoom1 = rng.Next(2) == 0;
        var componentStart = hostInRoom1 ? 0 : 6;
        var componentEnd = hostInRoom1 ? 6 : gridWidth;

        var damagedConduitCell = (X: rng.Next(componentStart, componentEnd), Y: rng.Next(0, GridDepth));

        var neighbors = new[]
        {
            (damagedConduitCell.X + 1, damagedConduitCell.Y),
            (damagedConduitCell.X - 1, damagedConduitCell.Y),
            (damagedConduitCell.X, damagedConduitCell.Y + 1),
            (damagedConduitCell.X, damagedConduitCell.Y - 1),
        }.Where(n => n.Item1 >= componentStart && n.Item1 < componentEnd && n.Item2 >= 0 && n.Item2 < GridDepth).ToList();

        var fireGeneratorCell = neighbors[rng.Next(neighbors.Count)];

        return (true, fireGeneratorCell, damagedConduitCell, hostInRoom1);
    }

    /// <summary>Candidate pool exactly mirrors the boundary edges ShipBuildTarget.SeedDefaultShipLayout
    /// itself walks (north/south full width, west/east at non-doorway rows) — deliberately
    /// excludes every RoomSplitColumns edge (see class doc comment on why). When a fire hazard
    /// exists, candidates whose cell shares its component are dropped entirely, so a breach can
    /// never vent the same room the fire hazard depends on having real air.</summary>
    private static List<ShipLayoutCatalog.BreachEdge> GenerateBreaches(Random rng, int gridWidth, bool hasFireHazard, bool fireHazardIsInRoom1)
    {
        var candidates = new List<ShipLayoutCatalog.BreachEdge>();

        for (var i = 0; i < gridWidth; i++)
        {
            candidates.Add(new ShipLayoutCatalog.BreachEdge { CellX = i, CellY = 0, OutsideX = i, OutsideY = -1 });
            candidates.Add(new ShipLayoutCatalog.BreachEdge { CellX = i, CellY = GridDepth - 1, OutsideX = i, OutsideY = GridDepth });
        }

        for (var j = 0; j < GridDepth; j++)
        {
            if (DoorwayRows.Contains(j))
            {
                continue;
            }

            candidates.Add(new ShipLayoutCatalog.BreachEdge { CellX = 0, CellY = j, OutsideX = -1, OutsideY = j });
            candidates.Add(new ShipLayoutCatalog.BreachEdge { CellX = gridWidth - 1, CellY = j, OutsideX = gridWidth, OutsideY = j });
        }

        if (hasFireHazard)
        {
            candidates = candidates.Where(edge => IsInRoom1(edge.CellX) != fireHazardIsInRoom1).ToList();
        }

        Shuffle(rng, candidates);

        var breachCount = Math.Min(rng.Next(MinBreaches, MaxBreachesExclusive), candidates.Count);
        return candidates.Take(breachCount).ToList();
    }

    private static List<LootSpawn> GenerateLoot(Random rng, int gridWidth, List<ShipLayoutCatalog.BreachEdge> breaches, bool hasFireHazard, (int X, int Y) fireGeneratorCell, (int X, int Y) damagedConduitCell)
    {
        var excluded = new HashSet<(int, int)>();
        foreach (var breach in breaches)
        {
            excluded.Add((breach.CellX, breach.CellY));
            foreach (var n in OrthogonalNeighbors(breach.CellX, breach.CellY))
            {
                excluded.Add(n);
            }
        }

        if (hasFireHazard)
        {
            excluded.Add(fireGeneratorCell);
            excluded.Add(damagedConduitCell);
            foreach (var n in OrthogonalNeighbors(fireGeneratorCell.X, fireGeneratorCell.Y))
            {
                excluded.Add(n);
            }

            foreach (var n in OrthogonalNeighbors(damagedConduitCell.X, damagedConduitCell.Y))
            {
                excluded.Add(n);
            }
        }

        var candidateTiles = new List<(int X, int Y)>();
        for (var i = 0; i < gridWidth; i++)
        {
            for (var j = 0; j < GridDepth; j++)
            {
                if (!excluded.Contains((i, j)))
                {
                    candidateTiles.Add((i, j));
                }
            }
        }

        Shuffle(rng, candidateTiles);

        var lootCount = Math.Min(rng.Next(MinLootCount, MaxLootCountExclusive), candidateTiles.Count);
        var loot = new List<LootSpawn>();
        var totalWeight = LootTable.Sum(entry => entry.Weight);

        for (var i = 0; i < lootCount; i++)
        {
            var roll = rng.Next(0, totalWeight);
            var cumulative = 0;
            var (itemId, _, minCount, maxCountExclusive) = LootTable[^1];
            foreach (var entry in LootTable)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                {
                    (itemId, _, minCount, maxCountExclusive) = entry;
                    break;
                }
            }

            var tile = candidateTiles[i];
            loot.Add(new LootSpawn(itemId, rng.Next(minCount, maxCountExclusive), tile.X, tile.Y));
        }

        return loot;
    }

    private static IEnumerable<(int, int)> OrthogonalNeighbors(int x, int y)
    {
        yield return (x + 1, y);
        yield return (x - 1, y);
        yield return (x, y + 1);
        yield return (x, y - 1);
    }

    private static void Shuffle<T>(Random rng, IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }
}
