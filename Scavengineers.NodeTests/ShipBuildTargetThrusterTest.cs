using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
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

    [TestCase]
    [RequireGodotRuntime]
    public async Task SeedDefaultShipLayout_InstallsTwoFullyFueledThrusters_AlreadyWiredIntoTheDefaultSpine()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { HasPowerGrid = true };
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget
        {
            ShipSimRef = shipSim,
            ShipRoot = shipRoot,
            SeedDefaultLayout = true,
            BatteryMesh = new BoxMesh(), // SeedDefaultShipLayout's own "opted into this system" gate.
        };
        shipRoot.AddChild(buildTarget);

        // SeedDefaultShipLayout runs via CallDeferred from _Ready — await past the flush, same
        // pattern TravelConsoleDerelictPresenceRaceTest already uses for its own deferred seeding.
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

        var thrusters = shipSim.Deck.Fixtures.OfType<ThrusterFixture>().ToList();

        AssertInt(thrusters.Count).IsEqual(2);
        AssertBool(thrusters.All(t => t.Condition >= 0.999f)).IsTrue();
        AssertBool(thrusters.All(t => t.PowerDraw == ShipSim.IdleDraw)).IsTrue();

        // Proves the seeded spur conduits (DefaultConduitRoute's new(3,1)/new(8,1) entries)
        // actually connect back to the seeded battery+switch, not just that the thrusters exist
        // visually — the automated version of "hook them up to the already existing wires."
        AssertBool(thrusters.All(t => shipSim.IsPowered(t.Id))).IsTrue();

        // Requirement 3: genuinely fueled, not just numerically charged — each engine's own
        // ThrusterVerbTarget.Contents holds a full physical n2_tank, not just Condition == 1.
        var thrusterNodes = buildTarget.GetChildren().OfType<ThrusterVerbTarget>().ToList();
        AssertInt(thrusterNodes.Count).IsEqual(2);
        AssertBool(thrusterNodes.All(t => t.Contents.Slots[0] is { ItemId: "n2_tank", Charge: >= 0.999f })).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SaveRoundTrip_PreservesBothTheFixturesConditionAndTheDockedTanksRemainingCharge()
    {
        var (buildTarget, _) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("thruster", 0, 0, 1, 0, "0.6|n2_tank|0.4")],
        });

        var captured = buildTarget.CaptureBuildState();
        var row = captured.Machines.Single(m => m.Type == "thruster");
        AssertBool(row.State == "0.6|n2_tank|0.4").IsTrue();

        var (buildTarget2, shipSim2) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget2.ApplyBuildState(captured);

        var thruster2 = buildTarget2.GetChildren().OfType<ThrusterVerbTarget>().Single();
        AssertFloat(shipSim2.Deck.Fixtures.OfType<ThrusterFixture>().Single().Condition).IsEqual(0.6f);
        AssertFloat(thruster2.Contents.Slots[0]!.Value.Charge).IsEqual(0.4f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Uninstall_RefundsAnyDockedTankIntoTheInventory()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, _) = MakeHarness(sceneTree);
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("thruster", 0, 0, 1, 0, "1|n2_tank|0.4")],
        });

        var thrusterNode = buildTarget.GetChildren().OfType<ThrusterVerbTarget>().Single();
        var inventory = new PlayerInventory();

        // ExecuteThrusterRemoval is internal to ShipBuildTarget (not visible from this assembly)
        // — go through the thruster's own public ExecuteVerb, same as every other machine
        // removal test in this suite already does (see ShipBuildTargetFixtureUpkeepTest).
        thrusterNode.ExecuteVerb(
            new Verb("uninstall_thruster", "VERB_UNINSTALL_THRUSTER", DurationSeconds: 0.2f) { IsDestructive = true },
            inventory);

        // Only starts the cycle timer — the actual removal (and refund) happens in
        // OnCycleComplete once the verb's own duration elapses.
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        AssertBool(inventory.Has("n2_tank", 1)).IsTrue();
    }
}
