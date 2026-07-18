using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ThrusterVerbTarget.AvailableVerbs — mirrors
/// BatteryVerbTargetTest's own Condition coverage, plus the Refuel verb's own full/not-full
/// gating (same shape as BatteryVerbTarget's RechargeVerb).</summary>
[TestSuite]
public class ThrusterVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsThisThrustersOwnN2Charge()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 0.65f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);

        AssertFloat(thruster.Condition!.Value).IsEqual(0.65f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRefuel_WhileBelowFullCharge_ButHidesItOnceFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 0.4f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);

        AssertBool(thruster.AvailableVerbs.Any(v => v.Id == "refuel_thruster")).IsTrue();

        shipSim.SetThrusterCharge("thruster_a", 1f);

        AssertBool(thruster.AvailableVerbs.Any(v => v.Id == "refuel_thruster")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ExecuteVerb_Refuel_SetsChargeToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 0.1f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);

        thruster.ExecuteVerb(new Scavengineers.Scripts.Verbs.Verb("refuel_thruster", "VERB_REFUEL_THRUSTER", DurationSeconds: 0.2f), inventory: null!);

        AssertFloat(shipSim.ThrusterChargeFraction("thruster_a")).IsEqual(1f);
    }
}
