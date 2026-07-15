using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipBuildTarget.GenerateAtmosphereZonesFromRoomLayout — the
/// procedural replacement for the Derelict's hand-placed ShipZoneRoom1/2/3/ShipZoneCorridorWest
/// nodes, so a data-driven layout (different GridWidth/RoomSplitColumns per derelict, see
/// ShipLayoutCatalog) gets correctly matching zones without needing hand-authored scene changes
/// per layout. Directly exercises the exact real derelict shape (GridWidth=18,
/// RoomSplitColumns=[6,12], WestCorridorLength=2) this project's earlier zone-tie-break bugs
/// were found and fixed against.</summary>
[TestSuite]
public class ShipBuildTargetAtmosphereZoneTest
{
    private static async Task<(ShipSim ShipSim, ShipBuildTarget BuildTarget, Node3D ShipRoot)> BuildRealDerelictShapeAsync(SceneTree sceneTree)
    {
        var shipRoot = new Node3D();
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { GridWidth = 18, RoomSplitColumns = [6, 12], WestCorridorLength = 2 };
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, GenerateAtmosphereZones = true };
        shipRoot.AddChild(buildTarget);

        // GenerateAtmosphereZonesFromRoomLayout runs via CallDeferred (ShipSimRef's own Deck may
        // not be built yet at AddChild time) — same two-step await InteriorDoorVerbTargetTest
        // already uses for its own CallDeferred-driven initial state.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        return (shipSim, buildTarget, shipRoot);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task GeneratesOneZonePerRoomBandPlusTheWestCorridor_WithTheExpectedRepresentativeTiles()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, _, shipRoot) = await BuildRealDerelictShapeAsync(sceneTree);

        var zones = shipRoot.GetChildren().OfType<ShipAtmosphereZone>().ToList();
        var tiles = zones.Select(z => z.Tile).ToHashSet();

        AssertInt(zones.Count).IsEqual(4);
        AssertBool(tiles.SetEquals(new[] { new Vector2I(2, 2), new Vector2I(8, 2), new Vector2I(14, 2), new Vector2I(-1, 2) })).IsTrue();

        shipRoot.QueueFree();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task EveryModeledCell_ResolvesToSomeZone_NoGapsInCoverage()
    {
        // The actual correctness requirement, independent of exact box padding values: nothing in
        // the ship's real Deck.Cells should ever fail to resolve to a zone (a fresh gap here would
        // reproduce the exact "0% O2 standing somewhere real" bug class this project already hit).
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (shipSim, _, shipRoot) = await BuildRealDerelictShapeAsync(sceneTree);

        foreach (var cell in shipSim.Deck.Cells)
        {
            var worldPosition = shipRoot.ToGlobal(new Vector3(cell.X - 3 + 0.5f, 1f, cell.Y - 3 + 0.5f));
            var found = ShipAtmosphereZone.FindZoneAt(shipRoot.GetWorld3D(), worldPosition);

            AssertObject(found).IsNotNull();
        }

        shipRoot.QueueFree();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Room1sZone_GetsTheCorridorSeamOverlapMargin_NotJustTheGenericPadding()
    {
        // Room1 (columns 0-5) is the band adjacent to the west airlock corridor — it must
        // reproduce the real, deliberately oversized zone this project's own earlier zone-tie-
        // break bug fixes hand-authored (center -2, spanning -7..3, not a "clean" -3.2..3.2), or
        // the same docking-seam misread bug could resurface for a data-driven derelict.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, _, shipRoot) = await BuildRealDerelictShapeAsync(sceneTree);

        var room1Zone = shipRoot.GetChildren().OfType<ShipAtmosphereZone>().Single(z => z.Tile == new Vector2I(2, 2));
        var shape = (BoxShape3D)room1Zone.GetChildren().OfType<CollisionShape3D>().Single().Shape!;

        var halfWidth = shape.Size.X / 2f;
        var leftEdge = room1Zone.Position.X - halfWidth;

        // A point standing right at the real docking seam (around world x=-3, the original
        // clean room boundary) must fall well inside Room1's zone, not just barely.
        AssertFloat(leftEdge).IsLess(-6f);

        shipRoot.QueueFree();
    }
}
