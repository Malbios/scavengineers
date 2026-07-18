using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipSim's power budget layer (DemandedPower/IsOverloaded) on
/// top of the existing pure-connectivity PowerSystem — a ship-wide brownout when total draw
/// exceeds BatteryCapacity, not a per-device priority cutoff, and never latched (clears itself the
/// instant demand drops back under capacity). Battery placed far from every ShipSim-auto-seeded
/// fixture (TravelConsole/InteriorDoor/StationAirlock/DerelictAirlock's own fixed cells) so this
/// suite's own hand-wired chain is the only thing actually connected — keeps DemandedPower's
/// numbers exactly predictable.</summary>
[TestSuite]
public class ShipSimPowerBudgetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void DemandedPower_SumsIdleDrawAcrossEveryConnectedDevice()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(shipSim);

        shipSim.InstallBattery(new CellCoord(20, 0), FixtureSurface.WallInner);
        shipSim.InstallThruster("t1", new CellCoord(21, 0), FixtureSurface.WallInner);
        shipSim.InstallThruster("t2", new CellCoord(22, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("t1", 1f);
        shipSim.SetThrusterCharge("t2", 1f);

        AssertFloat(shipSim.DemandedPower()).IsEqual(ShipSim.IdleDraw * 2);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ExceedingCapacity_MakesEveryFixtureUnpowered_NotJustTheOneOverIt()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(shipSim);

        shipSim.InstallBattery(new CellCoord(20, 0), FixtureSurface.WallInner);
        shipSim.InstallThruster("t1", new CellCoord(21, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("t1", 1f);

        AssertBool(shipSim.IsPowered("t1")).IsTrue();
        AssertBool(shipSim.IsPowered(ShipSim.BatteryFixtureId)).IsTrue();

        // Simulate a spike (what an active thruster/console/recharge draw would do) pushing total
        // demand over capacity by hand.
        shipSim.Deck.Fixtures.Single(f => f.Id == "t1").PowerDraw = ShipSim.BatteryCapacity + 1f;

        AssertBool(shipSim.IsPowered("t1")).IsFalse();
        AssertBool(shipSim.IsPowered(ShipSim.BatteryFixtureId)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DroppingBackUnderCapacity_RestoresPowerWithoutAnyManualReset()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(shipSim);

        shipSim.InstallBattery(new CellCoord(20, 0), FixtureSurface.WallInner);
        shipSim.InstallThruster("t1", new CellCoord(21, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("t1", 1f);

        var thruster = shipSim.Deck.Fixtures.Single(f => f.Id == "t1");
        thruster.PowerDraw = ShipSim.BatteryCapacity + 1f;
        AssertBool(shipSim.IsPowered("t1")).IsFalse();

        thruster.PowerDraw = ShipSim.IdleDraw;
        AssertBool(shipSim.IsPowered("t1")).IsTrue();
    }
}
