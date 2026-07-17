using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the Derelict's seeded hull breaches: they must use the
/// per-edge wall-breach API (Deck.IsWallEdgeBreached), not the older per-cell one, since
/// ShipBuildTarget's boundary-wall repair/removal verbs only read/write the per-edge set. A
/// prior mismatch here meant repairing these walls in-game never actually cleared the flag
/// AtmosphereSystem reads, so the room could never re-seal.</summary>
[TestSuite]
public class ShipSimTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void HullBreaches_AreTrackedPerEdge_NotPerCell()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(6, 5), new CellCoord(6, 6))).IsTrue();
        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(3, 0), new CellCoord(3, -1))).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void HasHullBreaches_GivesEveryFloorAndCeiling_ARoughStartingHealthBelowFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasHullBreaches = true });
        sceneTree.Root.AddChild(shipSim);

        foreach (var cell in shipSim.Deck.Cells)
        {
            AssertFloat(shipSim.Deck.FloorHealth(cell)).IsLess(1f);
            AssertFloat(shipSim.Deck.FloorHealth(cell)).IsGreaterEqual(0.3f);
            AssertFloat(shipSim.Deck.CeilingHealth(cell)).IsLess(1f);
            AssertFloat(shipSim.Deck.CeilingHealth(cell)).IsGreaterEqual(0.3f);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void NoHullBreaches_LeavesEveryFloorAndCeiling_AtPristineFullHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim()); // HasHullBreaches defaults false (e.g. the Home Ship)
        sceneTree.Root.AddChild(shipSim);

        foreach (var cell in shipSim.Deck.Cells)
        {
            AssertFloat(shipSim.Deck.FloorHealth(cell)).IsEqual(1f);
            AssertFloat(shipSim.Deck.CeilingHealth(cell)).IsEqual(1f);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void VolumeAt_ForACellPastThisShipsOwnModeledCorridorLength_ReadsTheNearestModeledNeighbor()
    {
        // Reproduces the crash hit crossing a live airlock: ShipAtmosphereZone.TileAt derives a
        // tile straight from world position, and the physical corridor/threshold mesh between two
        // docked ships (including a closed door's own boundary edge) can land one tile past
        // whichever ship's own WestCorridorLength/EastCorridorLength the player is currently
        // standing over. AtmosphereSystem.VolumeAt throws KeyNotFoundException for a cell it never
        // modeled at all — ShipSim.VolumeAt must catch that case itself rather than let it reach
        // the caller. It must also read as this ship's own real (breathable) air here, not a
        // blanket Vacuum — the earlier "always Vacuum" fallback was what caused a real bug
        // (standing at a closed door misreading 0% O2 from the ship on the other side).
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { WestCorridorLength = 2 });
        sceneTree.Root.AddChild(shipSim);

        var oneCellPastTheCorridor = new CellCoord(-3, 3);
        var nearestModeledNeighbor = new CellCoord(-2, 3);

        AssertBool(shipSim.Deck.Cells.Contains(oneCellPastTheCorridor)).IsFalse();
        AssertBool(shipSim.Deck.Cells.Contains(nearestModeledNeighbor)).IsTrue();
        AssertFloat(shipSim.VolumeAt(oneCellPastTheCorridor).O2Fraction)
            .IsEqual(shipSim.VolumeAt(nearestModeledNeighbor).O2Fraction);
        AssertFloat(shipSim.VolumeAt(oneCellPastTheCorridor).O2Fraction)
            .IsEqual(AtmosphereVolume.Breathable.O2Fraction);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void VolumeAt_ReturnsVacuum_WhenNoModeledNeighborExistsEither()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var farFromAnyModeledCell = new CellCoord(50, 50);

        AssertBool(shipSim.Deck.Cells.Contains(farFromAnyModeledCell)).IsFalse();
        AssertFloat(shipSim.VolumeAt(farFromAnyModeledCell).O2Fraction).IsEqual(AtmosphereVolume.Vacuum.O2Fraction);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void VolumeAt_AtTheSeamNextToABreach_StillReadsVacuum()
    {
        // Confirms the original crash-prevention scenario doesn't regress: when the nearest
        // modeled neighbor is itself vented (a real breach nearby), the seam cell still correctly
        // reads Vacuum — same observable result as the old blanket-Vacuum fallback gave for this
        // case, just via the neighbor's own real (also-Vacuum) state instead of a hardcoded value.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { WestCorridorLength = 2 });
        sceneTree.Root.AddChild(shipSim);

        var nearestModeledNeighbor = new CellCoord(-2, 3);
        shipSim.Deck.BreachHull(nearestModeledNeighbor);
        for (var i = 0; i < 50; i++)
        {
            shipSim.Atmosphere!.Tick(1);
        }

        var oneCellPastTheCorridor = new CellCoord(-3, 3);

        AssertFloat(shipSim.VolumeAt(oneCellPastTheCorridor).O2Fraction).IsEqual(AtmosphereVolume.Vacuum.O2Fraction);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyLayout_OverwritesGridShapeAndHazards_FromAGivenDefinition()
    {
        // Constructed directly (not via ShipLayoutCatalog) — NodeTests is a separate assembly
        // that can neither reach res://Data/Ships/layouts.json nor call the catalog's internal
        // SeedForTests, so ShipLayoutDefinition is public specifically so this test can build
        // one by hand (see ShipLayoutCatalog's own doc comment).
        var layout = new ShipLayoutCatalog.ShipLayoutDefinition
        {
            Id = "test_small",
            GridWidth = 12,
            RoomSplitColumns = [6],
            HasHullBreaches = true,
            HasFireHazard = true,
            InitialBreaches = [new() { CellX = 9, CellY = 5, OutsideX = 9, OutsideY = 6 }],
            FireGeneratorCell = new() { X = 2, Y = 4 },
            DamagedConduitCell = new() { X = 2, Y = 3 },
        };

        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { GridWidth = 18, RoomSplitColumns = [6, 12] });
        shipSim.ApplyLayout(layout);
        sceneTree.Root.AddChild(shipSim);

        AssertBool(shipSim.Deck.Cells.Contains(new CellCoord(15, 3))).IsFalse();
        AssertBool(shipSim.Deck.Cells.Contains(new CellCoord(9, 3))).IsTrue();
        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(9, 5), new CellCoord(9, 6))).IsTrue();
        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(6, 5), new CellCoord(6, 6))).IsFalse();
        AssertObject(shipSim.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.FireGeneratorFixtureId)).IsNotNull();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ProcedurallyGenerate_ProducesAValidRandomShip_RespectingTheColumn6Invariant()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { ProcedurallyGenerate = true });
        sceneTree.Root.AddChild(shipSim);

        AssertBool(shipSim.Deck.Cells.Count > 0).IsTrue();
        AssertInt(shipSim.RoomSplitColumns[0]).IsEqual(6);
        // Room 1 (columns 0-5) must always be a real, sealed-off room regardless of what the
        // rest of the ship rolled.
        AssertBool(shipSim.Deck.IsEdgeSealed(new CellCoord(5, 0), new CellCoord(6, 0))).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ProcedurallyGenerate_WinsOverLayoutId_WhenBothAreSet()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { ProcedurallyGenerate = true, LayoutId = "derelict_1" });
        sceneTree.Root.AddChild(shipSim);

        // NodeTests' own isolated res:// has no Data/Ships/layouts.json (see the
        // project_nodetests_isolated_project memory), so ShipLayoutCatalog.TryGet("derelict_1")
        // would always return null there — if LayoutId's path had won instead, ApplyLayout(null)
        // is a no-op and HasHullBreaches would stay false forever. The generator always sets
        // HasHullBreaches = true, so this is a fully deterministic (not probabilistic) signal
        // that ProcedurallyGenerate actually took priority.
        AssertBool(shipSim.HasHullBreaches).IsTrue();
        AssertInt(shipSim.RoomSplitColumns[0]).IsEqual(6);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void LayoutId_LeftUnset_KeepsTodaysOriginalHardcodedHazardPlacement()
    {
        // Guards against a future accidental default-value edit silently changing the
        // no-LayoutId fallback — every existing scene (Home Ship, Station, and any Derelict
        // instance without a LayoutId override) must keep behaving exactly as before this
        // system existed.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasHullBreaches = true, HasFireHazard = true });
        sceneTree.Root.AddChild(shipSim);

        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(6, 5), new CellCoord(6, 6))).IsTrue();
        AssertBool(shipSim.Deck.IsWallEdgeBreached(new CellCoord(3, 0), new CellCoord(3, -1))).IsTrue();
        AssertObject(shipSim.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.FireGeneratorFixtureId)?.Tile)
            .IsEqual(new CellCoord(1, 4));
        AssertObject(shipSim.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.DamagedConduitFixtureId)?.Tile)
            .IsEqual(new CellCoord(1, 3));
    }
}
