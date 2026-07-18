using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for _placedThrusters' own edge-normalization dedup (the exact bug
/// a plan-review pass caught before this feature was built: aiming at the same interior wall from
/// either side produces a reversed CellCoord pair) and the save round-trip capturing/restoring
/// several thrusters at independent charges.</summary>
[TestSuite]
public class ShipBuildTargetThrusterTest
{
    private static (ShipBuildTarget BuildTarget, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        return (buildTarget, shipSim);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void InstallThruster_NotOfferedAgain_WhenAimingFromEitherSideOfAnAlreadyInstalledThruster()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.BatteryMesh = new BoxMesh(); // MachineVerbsFor's own "opted into this system" gate.

        var edgeA = new CellCoord(0, 0);
        var edgeB = new CellCoord(1, 0);
        shipSim.Deck.SealEdge(edgeA, edgeB); // A machine needs a real wall to mount on.

        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("thruster", 0, 0, 1, 0, "1")],
        });

        // Aimed from inside cell (0,0) looking toward (1,0) — resolves _edgeA=(0,0), _edgeB=(1,0).
        buildTarget.SetAimPoint(new Vector3(-2.1f, 0f, -2.5f));
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "install_thruster")).IsFalse();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "uninstall_thruster")).IsTrue();

        // Aimed from inside cell (1,0) looking back toward (0,0) — the raw pair is reversed
        // (_edgeA=(1,0), _edgeB=(0,0)), exactly the case Deck.Normalize exists to dedupe.
        buildTarget.SetAimPoint(new Vector3(-1.9f, 0f, -2.5f));
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "install_thruster")).IsFalse();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "uninstall_thruster")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void CaptureBuildState_EmitsAThrusterRow_PerInstalledThruster_WithItsOwnCharge()
    {
        var (buildTarget, _) = MakeHarness((SceneTree)Engine.GetMainLoop());

        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines =
            [
                new MachineCoord("thruster", 0, 0, 1, 0, "0.6"),
                new MachineCoord("thruster", 2, 0, 3, 0, "0.25"),
            ],
        });

        var thrusterRows = buildTarget.CaptureBuildState().Machines.Where(m => m.Type == "thruster").ToList();

        AssertInt(thrusterRows.Count).IsEqual(2);
        AssertBool(thrusterRows.Any(m => m.EdgeAX == 0 && m.EdgeAY == 0 && m.EdgeBX == 1 && m.EdgeBY == 0 && m.State == "0.6")).IsTrue();
        AssertBool(thrusterRows.Any(m => m.EdgeAX == 2 && m.EdgeAY == 0 && m.EdgeBX == 3 && m.EdgeBY == 0 && m.State == "0.25")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplyBuildState_RestoresEveryThrustersOwnChargeFraction_FromACapturedSnapshot()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines =
            [
                new MachineCoord("thruster", 0, 0, 1, 0, "0.6"),
                new MachineCoord("thruster", 2, 0, 3, 0, "0.25"),
            ],
        });

        var captured = buildTarget.CaptureBuildState();

        var (buildTarget2, shipSim2) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget2.ApplyBuildState(captured);

        var reloadedCharges = shipSim2.Deck.Fixtures.OfType<ThrusterFixture>()
            .Select(f => f.Condition)
            .OrderBy(c => c)
            .ToList();

        AssertInt(reloadedCharges.Count).IsEqual(2);
        AssertFloat(reloadedCharges[0]).IsEqual(0.25f);
        AssertFloat(reloadedCharges[1]).IsEqual(0.6f);
    }
}
