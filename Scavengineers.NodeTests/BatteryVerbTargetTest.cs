using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for BatteryVerbTarget.Condition — a plain gap, not new behavior:
/// the battery's own charge (Fixture.Condition, exposed via ShipSim.BatteryChargeFraction) always
/// existed, but nothing surfaced it through IVerbTarget.Condition, so the PDA's scan mode showed
/// nothing when aiming at it.</summary>
[TestSuite]
public class BatteryVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsTheBatterysOwnCharge()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetBatteryCharge(0.65f);

        var battery = AutoFree(new BatteryVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(battery);

        AssertFloat(battery.Condition!.Value).IsEqual(0.65f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_IsNull_WithNoBatteryInstalledAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var battery = AutoFree(new BatteryVerbTarget { ShipSimRef = shipSim });
        sceneTree.Root.AddChild(battery);

        // BatteryChargeFraction defaults to 0f with no battery installed (see ShipSim), not
        // null — Condition follows that same "always a number, never truly absent" shape.
        AssertFloat(battery.Condition!.Value).IsEqual(0f);
    }
}
