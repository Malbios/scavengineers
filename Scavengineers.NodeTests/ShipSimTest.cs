using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
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
}
