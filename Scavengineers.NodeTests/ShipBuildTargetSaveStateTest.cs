using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for Stage 6's save-schema additions: structural (floor/ceiling/
/// wall) health, a placed conduit fixture's own Condition, and a Switch/RechargeStation machine's
/// Condition all used to reset to full health on every load (ApplyBuildState never restored them,
/// and CaptureBuildState never wrote them) — this is the exact gap that closes.</summary>
[TestSuite]
public class ShipBuildTargetSaveStateTest
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task ApplyThenCaptureBuildState_RoundTripsStructuralHealthConduitAndMachineCondition()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, PanelMesh = new BoxMesh() };
        shipRoot.AddChild(buildTarget);

        // Lets GenerateFloorCeilingPanels' CallDeferred populate _floorPanels/_ceilingPanels
        // before ApplyBuildState/CaptureBuildState iterate them.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

        var data = new BuildTargetSaveData
        {
            Walls = [new EdgeCoord(0, 0, 1, 0)],
            Conduits = [new TileCoord(0, 0)],
            Machines = [new MachineCoord("switch", 0, 0, 1, 0, null, 0.4f)],
            FloorHealthEntries = [new TileHealthCoord(2, 2, 0.6f)],
            CeilingHealthEntries = [new TileHealthCoord(2, 2, 0.55f)],
            WallHealthEntries = [new EdgeHealthCoord(0, 0, 1, 0, 0.65f)],
            ConduitConditions = new System.Collections.Generic.Dictionary<string, float> { ["player_conduit_0_0_floor"] = 0.5f },
        };

        buildTarget.ApplyBuildState(data);

        AssertFloat(shipSim.Deck.FloorHealth(new CellCoord(2, 2))).IsEqual(0.6f);
        AssertFloat(shipSim.Deck.CeilingHealth(new CellCoord(2, 2))).IsEqual(0.55f);
        AssertFloat(shipSim.Deck.WallHealth(new CellCoord(0, 0), new CellCoord(1, 0))).IsEqual(0.65f);
        AssertFloat(shipSim.Deck.Fixtures.Single(f => f.Id == "player_conduit_0_0_floor").Condition).IsEqual(0.5f);
        AssertFloat(shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.SwitchFixtureId).Condition).IsEqual(0.4f);

        var captured = buildTarget.CaptureBuildState();

        AssertBool(captured.FloorHealthEntries.Any(e => e is { X: 2, Y: 2 } && Mathf.IsEqualApprox(e.Health, 0.6f))).IsTrue();
        AssertBool(captured.CeilingHealthEntries.Any(e => e is { X: 2, Y: 2 } && Mathf.IsEqualApprox(e.Health, 0.55f))).IsTrue();
        AssertBool(captured.WallHealthEntries.Any(e => e is { AX: 0, AY: 0, BX: 1, BY: 0 } && Mathf.IsEqualApprox(e.Health, 0.65f))).IsTrue();
        AssertBool(captured.ConduitConditions.TryGetValue("player_conduit_0_0_floor", out var conduitCondition) && Mathf.IsEqualApprox(conduitCondition, 0.5f)).IsTrue();

        var switchMachine = captured.Machines.Single(m => m.Type == "switch");
        AssertBool(Mathf.IsEqualApprox(switchMachine.Condition, 0.4f)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task CaptureBuildState_OmitsHealthEntries_ForAnythingAtOrEssentiallyAtFullHealth()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, PanelMesh = new BoxMesh() };
        shipRoot.AddChild(buildTarget);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Walls = [new EdgeCoord(0, 0, 1, 0)],
            Conduits = [new TileCoord(0, 0)],
            Machines = [new MachineCoord("switch", 0, 0, 1, 0, null)],
        });

        var captured = buildTarget.CaptureBuildState();

        // Not asserting these lists are exactly empty: ShipSim's own WearSystem passively decays
        // every cell/fixture a tiny amount on every physics tick (including the one the awaited
        // frame above let run), so a handful of near-1.0 entries may legitimately appear. What
        // matters is that nothing here is *meaningfully* damaged — the <1f gate isn't leaking
        // full-health noise into the save on a scale a player would ever notice.
        AssertBool(captured.FloorHealthEntries.All(e => e.Health > 0.999f)).IsTrue();
        AssertBool(captured.CeilingHealthEntries.All(e => e.Health > 0.999f)).IsTrue();
        AssertBool(captured.WallHealthEntries.All(e => e.Health > 0.999f)).IsTrue();
        AssertBool(captured.ConduitConditions.Values.All(v => v > 0.999f)).IsTrue();
        AssertBool(captured.Machines.Single(m => m.Type == "switch").Condition > 0.999f).IsTrue();
    }
}
