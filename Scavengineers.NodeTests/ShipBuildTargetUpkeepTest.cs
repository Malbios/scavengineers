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

/// <summary>Regression coverage for ShipBuildTarget's structural Maintain/Repair verbs (Stage 5
/// of the object-health/PDA-scanner feature): the Ostranauts-style two-tier rule (>50% health ->
/// free Maintain, tool only; <=50% -> Repair, tool + spare_parts) applied to floor/ceiling/wall,
/// plus the Condition readout the PDA's scan mode reads. Fixtures/machines are a separate,
/// not-yet-started follow-up stage.</summary>
[TestSuite]
public class ShipBuildTargetUpkeepTest
{
    // Cell (0,0)'s tile center in ship-root-local space: X = 0 - 3 + 0.5, Z = 0 - 3 + 0.5.
    private static readonly Vector3 CellZeroZeroCenter = new(-2.5f, 0f, -2.5f);

    private static (ShipBuildTarget BuildTarget, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, PanelMesh = new BoxMesh() };
        shipRoot.AddChild(buildTarget);

        return (buildTarget, shipSim);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersNoUpkeepVerb_AtFullFloorHealth()
    {
        var (buildTarget, _) = MakeHarness((SceneTree)Engine.GetMainLoop());

        buildTarget.SetAimPoint(CellZeroZeroCenter);

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id is "maintain_floor" or "repair_floor")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintainFloor_AboveHalfHealth()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        shipSim.Deck.DamageFloor(new CellCoord(0, 0), 0.3f); // 1.0 -> 0.7

        buildTarget.SetAimPoint(CellZeroZeroCenter);

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "maintain_floor")).IsTrue();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "repair_floor")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersRepairFloor_AtOrBelowHalfHealth()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        shipSim.Deck.DamageFloor(new CellCoord(0, 0), 0.5f); // 1.0 -> 0.5, the boundary itself

        buildTarget.SetAimPoint(CellZeroZeroCenter);

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "repair_floor")).IsTrue();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "maintain_floor")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintainCeiling_AboveHalfHealth()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        shipSim.Deck.DamageCeiling(new CellCoord(0, 0), 0.3f); // 1.0 -> 0.7

        buildTarget.SetCeilingAimPoint(CellZeroZeroCenter);

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "maintain_ceiling")).IsTrue();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "repair_ceiling")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AvailableVerbs_OffersMaintainWall_ForADamagedSealedInteriorEdge()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        var a = new CellCoord(0, 0);
        var b = new CellCoord(1, 0);
        shipSim.Deck.SealEdge(a, b);
        shipSim.Deck.DamageWall(a, b, 0.3f); // 1.0 -> 0.7

        // Aimed near the shared edge between (0,0) and (1,0): local X close to -2 (fx close to 1).
        buildTarget.SetAimPoint(new Vector3(-2.1f, 0f, -2.5f));

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "maintain_wall")).IsTrue();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "repair_wall")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_MaintainFloor_RestoresFloorHealthToFull_OnCycleCompletion()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, shipSim) = MakeHarness(sceneTree);
        var cell = new CellCoord(0, 0);
        shipSim.Deck.DamageFloor(cell, 0.3f); // 1.0 -> 0.7

        buildTarget.SetAimPoint(CellZeroZeroCenter);
        buildTarget.ExecuteVerb(new Verb("maintain_floor", "VERB_MAINTAIN_FLOOR", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background (including the ones this await let run), same as any other cell.
        AssertFloat(shipSim.Deck.FloorHealth(cell)).IsGreater(0.999f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_RepairWall_RestoresWallHealthToFull_OnCycleCompletion()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, shipSim) = MakeHarness(sceneTree);
        var a = new CellCoord(0, 0);
        var b = new CellCoord(1, 0);
        shipSim.Deck.SealEdge(a, b);
        shipSim.Deck.DamageWall(a, b, 0.7f); // 1.0 -> 0.3, damaged tier

        buildTarget.SetAimPoint(new Vector3(-2.1f, 0f, -2.5f));
        buildTarget.ExecuteVerb(new Verb("repair_wall", "VERB_REPAIR_WALL", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: ShipSim's own WearSystem keeps passively decaying every physics tick
        // in the background (including the ones this await let run), same as any other edge.
        AssertFloat(shipSim.Deck.WallHealth(a, b)).IsGreater(0.999f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsTheAimedFloorsHealth_AndIsNullOnceBreached()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        var cell = new CellCoord(0, 0);
        shipSim.Deck.DamageFloor(cell, 0.4f); // 1.0 -> 0.6

        buildTarget.SetAimPoint(CellZeroZeroCenter);
        AssertFloat(buildTarget.Condition!.Value).IsEqual(0.6f);

        shipSim.Deck.BreachHull(cell, StructuralSurface.Floor);
        buildTarget.SetAimPoint(CellZeroZeroCenter);
        AssertObject(buildTarget.Condition).IsNull();
    }
}
