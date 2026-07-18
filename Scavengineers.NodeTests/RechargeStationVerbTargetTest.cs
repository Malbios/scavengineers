using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for RechargeStationVerbTarget's own power-draw spike — Recharge
/// is a near-instant 0.2s action with no other "in progress" signal, so its own fixture's
/// PowerDraw is set directly at ExecuteVerb time and decays back to idle via a short one-shot
/// Timer instead of a sustained continuous state.</summary>
[TestSuite]
public class RechargeStationVerbTargetTest
{
    private static (RechargeStationVerbTarget Station, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { HasPowerGrid = true };
        shipRoot.AddChild(shipSim);

        shipSim.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        shipSim.InstallRechargeStation(new CellCoord(1, 0), FixtureSurface.WallInner);

        var station = new RechargeStationVerbTarget { ShipSimRef = shipSim };
        shipRoot.AddChild(station);

        return (station, shipSim);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void NewlyInstalled_StartsAtIdleDraw()
    {
        var (_, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());

        var fixture = shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.RechargeFixtureId);

        AssertFloat(fixture.PowerDraw).IsEqual(ShipSim.IdleDraw);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_Recharge_SpikesDrawThenDecaysBackToIdle()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (station, shipSim) = MakeHarness(sceneTree);
        var fixture = shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.RechargeFixtureId);

        station.ExecuteVerb(new Verb("recharge", "VERB_RECHARGE", DurationSeconds: 0.2f), inventory: null!);

        AssertFloat(fixture.PowerDraw).IsEqual(ShipSim.RechargeStationActiveDraw);

        // Past the 1.5s active-draw window with real margin.
        await sceneTree.ToSignal(sceneTree.CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(fixture.PowerDraw).IsEqual(ShipSim.IdleDraw);
    }
}
