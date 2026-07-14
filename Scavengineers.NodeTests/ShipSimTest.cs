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
    public void VolumeAt_ReturnsVacuum_ForACellPastThisShipsOwnModeledCorridorLength()
    {
        // Reproduces the crash hit crossing a live airlock: ShipAtmosphereZone.TileAt derives a
        // tile straight from world position, and the physical corridor/threshold mesh between two
        // docked ships can extend one tile past whichever ship's own WestCorridorLength/
        // EastCorridorLength the player is currently standing over. AtmosphereSystem.VolumeAt
        // throws KeyNotFoundException for a cell it never modeled at all — ShipSim.VolumeAt must
        // catch that case itself rather than let it reach the caller.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { WestCorridorLength = 2 });
        sceneTree.Root.AddChild(shipSim);

        var oneCellPastTheCorridor = new CellCoord(-3, 3);

        AssertBool(shipSim.Deck.Cells.Contains(oneCellPastTheCorridor)).IsFalse();
        AssertBool(shipSim.VolumeAt(oneCellPastTheCorridor).O2Fraction == AtmosphereVolume.Vacuum.O2Fraction).IsTrue();
    }
}
