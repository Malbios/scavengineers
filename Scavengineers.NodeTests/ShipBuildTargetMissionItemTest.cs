using System;
using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipBuildTarget.SpawnMissionItem/PlaceMissionItem — the fix for
/// RetrieveItem contracts never actually placing their target item anywhere (see
/// ContractGiverVerbTarget.TryTakeOffer's own doc comment). Distinct from
/// ShipBuildTargetLootTest (procedural GenerateLoot from ShipSim.LootSpawns): this is a one-off
/// spawn triggered by contract acceptance, on a live, non-vented tile specifically.</summary>
[TestSuite]
public class ShipBuildTargetMissionItemTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void SpawnMissionItem_LandsOnTheOnlyLiveCell_WhenEveryOtherCellIsVented()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, SaveId = "derelict_test" };
        shipRoot.AddChild(buildTarget);

        // A bare Deck (no SeedDefaultLayout) has no sealed edges at all — every cell is one big
        // connected blob by default, so venting ANY cell vents the whole ship. To get a genuine
        // single surviving live cell, (0,0)'s own 2 edges (a grid corner only has 2 neighbors)
        // are sealed off first, isolating it into its own component; breaching a single cell
        // elsewhere then vents the entire REST of the blob, leaving (0,0) as the only cell
        // SpawnMissionItem could possibly land on, regardless of the rng seed.
        var liveCell = new CellCoord(0, 0);
        shipSim.Deck.SealEdge(liveCell, new CellCoord(1, 0));
        shipSim.Deck.SealEdge(liveCell, new CellCoord(0, 1));
        shipSim.Deck.BreachWallEdge(new CellCoord(5, 3), new CellCoord(999, 999));

        buildTarget.SpawnMissionItem("o2_tank", 1, new Random(123));

        var pickup = shipRoot.GetChildren().OfType<PickupItem>().Single();
        AssertString(pickup.ItemId).IsEqual("o2_tank");
        AssertInt(pickup.Count).IsEqual(1);
        AssertString(pickup.MissionOwnerSaveId).IsEqual("derelict_test");
        AssertFloat(pickup.Position.X).IsEqual(liveCell.X - 3 + 0.5f);
        AssertFloat(pickup.Position.Z).IsEqual(liveCell.Y - 3 + 0.5f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SpawnMissionItem_StillSpawnsExactlyOne_WhenTheWholeShipIsVented()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        // A bare Deck (no SeedDefaultLayout) has no sealed edges — every cell is one connected
        // blob, so a single breach anywhere vents the whole ship (see the sibling test's own doc
        // comment for the full reasoning).
        shipSim.Deck.BreachWallEdge(new CellCoord(5, 3), new CellCoord(999, 999));

        buildTarget.SpawnMissionItem("o2_tank", 1, new Random(7));

        AssertInt(shipRoot.GetChildren().OfType<PickupItem>().Count()).IsEqual(1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PlaceMissionItem_OnAnAlreadyHiddenShip_SpawnsAHiddenDecollidedPhysicsPausedPickup()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();

        // Mirrors an already-decollided-and-hidden Derelict (see
        // TravelConsoleVerbTarget.SetShipPresence) — this is exactly the state a real derelict
        // sits in whenever it isn't the current destination, which is when a contract targeting
        // it would actually run this spawn.
        var shipRoot = AutoFree(new Node3D { Visible = false });
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        buildTarget.PlaceMissionItem("o2_tank", 1, 1f, Vector3.Zero);

        var pickup = shipRoot.GetChildren().OfType<PickupItem>().Single();
        AssertBool(pickup.Visible).IsFalse();

        var collisionShapes = pickup.FindChildren("*", nameof(CollisionShape3D), recursive: true, owned: false)
            .OfType<CollisionShape3D>()
            .ToList();
        AssertBool(collisionShapes.Count > 0).IsTrue();
        AssertBool(collisionShapes.All(shape => shape.Disabled)).IsTrue();

        // SetPhysicsPresence(false) — see PickupItem's own doc comment — stops physics processing
        // entirely rather than leaving it live with nothing underneath it to rest on.
        AssertBool(pickup.IsPhysicsProcessing()).IsFalse();
    }
}
