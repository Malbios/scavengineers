using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ThrusterVerbTarget.AvailableVerbs and its physical N2 tank
/// dock (Contents) — a real item sitting in a slot that continuously feeds the fixture's own
/// charge and drains itself in the process, replacing the old one-shot Refuel verb.</summary>
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
    public void AvailableVerbs_AlwaysOffersOpen()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);

        // Unconditional — unlike the old Refuel verb, there's no "already full" hiding rule for
        // opening the tank slot itself.
        shipSim.SetThrusterCharge("thruster_a", 1f);
        AssertBool(thruster.AvailableVerbs.Any(v => v.Id == "open_thruster")).IsTrue();

        shipSim.SetThrusterCharge("thruster_a", 0f);
        AssertBool(thruster.AvailableVerbs.Any(v => v.Id == "open_thruster")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task DockedTank_ContinuouslyFeedsTheFixture_AndDrainsItself()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 0.5f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);
        thruster.Contents.Add("n2_tank", 1, charge: 0.8f);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(shipSim.ThrusterChargeFraction("thruster_a")).IsGreater(0.5f);
        AssertFloat(thruster.Contents.Slots[0]!.Value.Charge).IsLess(0.8f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task DockedTank_StopsTransferring_OnceTheFixtureIsAlreadyFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 1f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);
        thruster.Contents.Add("n2_tank", 1, charge: 1f);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // A full fixture doesn't drain a spare tank just because it's docked — the tank keeps
        // its own charge for whenever the fixture actually needs it.
        AssertFloat(thruster.Contents.Slots[0]!.Value.Charge).IsEqual(1f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task EmptyDockedTank_DoesNothing_NoCrash()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);
        shipSim.InstallThruster("thruster_a", new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.SetThrusterCharge("thruster_a", 0.3f);

        var thruster = AutoFree(new ThrusterVerbTarget { ShipSimRef = shipSim, FixtureId = "thruster_a" });
        sceneTree.Root.AddChild(thruster);
        thruster.Contents.Add("n2_tank", 1, charge: 0f);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(shipSim.ThrusterChargeFraction("thruster_a")).IsEqual(0.3f);
        AssertFloat(thruster.Contents.Slots[0]!.Value.Charge).IsEqual(0f);
    }
}
