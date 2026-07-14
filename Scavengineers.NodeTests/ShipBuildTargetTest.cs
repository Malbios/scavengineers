using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ActiveBreachPositions() missing a whole class of breach:
/// AirlockDoorVerbTarget (and anything else using Deck.BreachHull's default Wall surface as a
/// per-CELL "this whole cell is exposed to vacuum" flag, not a specific removed wall segment)
/// produced no pull-target position at all, since the method only checked Floor/Ceiling per-cell
/// and Wall per-EDGE (WallEdgeBreaches) — never Wall per-cell.</summary>
[TestSuite]
public class ShipBuildTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void ActiveBreachPositions_IncludesAPerCellWallBreach_LikeAnAirlockVentingToSpace()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var cell = new CellCoord(0, 0);
        shipSim.Deck.BreachHull(cell); // default surface: Wall, per-cell — matches AirlockDoorVerbTarget.SetBreached

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        AssertBool(buildTarget.ActiveBreachPositions().Count() == 1).IsTrue();
    }
}
