using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipAtmosphereZone's real physics zero-g override (loose
/// pickup items drifting in a breached room) — a room reading as vacuum must flip the zone's own
/// Area3D gravity override on, and a normal breathable room must leave it off.</summary>
[TestSuite]
public class ShipAtmosphereZoneTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void EnablesGravityOverride_OnceRoomReadsAsVacuum()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var cell = new CellCoord(0, 0);
        shipSim.Deck.BreachHull(cell);
        for (var i = 0; i < 50; i++)
        {
            shipSim.Atmosphere!.Tick(1);
        }

        var zone = AutoFree(new ShipAtmosphereZone { ShipSimRef = shipSim, Tile = new Vector2I(0, 0) });
        sceneTree.Root.AddChild(zone);

        zone._PhysicsProcess(0);

        AssertBool(zone.GravitySpaceOverride == Area3D.SpaceOverride.Replace).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DisablesGravityOverride_InBreathableRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var zone = AutoFree(new ShipAtmosphereZone { ShipSimRef = shipSim, Tile = new Vector2I(0, 0) });
        sceneTree.Root.AddChild(zone);

        zone._PhysicsProcess(0);

        AssertBool(zone.GravitySpaceOverride == Area3D.SpaceOverride.Disabled).IsTrue();
    }

    private static ShipAtmosphereZone MakeZoneWithShape(SceneTree sceneTree, ShipSim shipSim, Vector2I tile, Vector3 position, Vector3 shapeSize)
    {
        var zone = new ShipAtmosphereZone
        {
            ShipSimRef = shipSim,
            Tile = tile,
            Transform = new Transform3D(Basis.Identity, position),
        };
        sceneTree.Root.AddChild(zone);
        var collision = new CollisionShape3D { Shape = new BoxShape3D { Size = shapeSize } };
        zone.AddChild(collision);
        return zone;
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task FindZoneAt_FindsTheZoneWhoseShapeContainsThePosition()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var zone = AutoFree(MakeZoneWithShape(
            sceneTree, shipSim, new Vector2I(5, 5), new Vector3(10, 1, 0), new Vector3(4, 2, 2)));

        // A newly-added CollisionShape3D only registers with the physics server on the next
        // physics step — querying it in the same frame it was added finds nothing yet.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var found = ShipAtmosphereZone.FindZoneAt(zone.GetWorld3D(), new Vector3(10, 1, 0));

        AssertBool(ReferenceEquals(found, zone)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void FindZoneAt_ReturnsNull_WhenNoZoneCoversThePosition()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var zone = AutoFree(MakeZoneWithShape(
            sceneTree, shipSim, new Vector2I(5, 5), new Vector3(10, 1, 0), new Vector3(4, 2, 2)));

        var found = ShipAtmosphereZone.FindZoneAt(zone.GetWorld3D(), new Vector3(1000, 1, 0));

        AssertObject(found).IsNull();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task FindZoneAt_InOverlapBand_PicksWhicheverZonesShapeActuallyContainsThePosition()
    {
        // Mirrors the real airlock-threshold layout: two zones (e.g. one per ship) whose shapes
        // deliberately overlap by a margin — the position itself, not "last entered", decides
        // which zone answers, so there's no stale-tracking risk crossing between them.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var zoneA = AutoFree(MakeZoneWithShape(
            sceneTree, shipA, new Vector2I(1, 1), new Vector3(10, 1, 0), new Vector3(4, 2, 2))); // spans x[8,12]
        var zoneB = AutoFree(MakeZoneWithShape(
            sceneTree, shipB, new Vector2I(2, 2), new Vector3(12, 1, 0), new Vector3(4, 2, 2))); // spans x[10,14]

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var deepInA = ShipAtmosphereZone.FindZoneAt(zoneA.GetWorld3D(), new Vector3(8.5f, 1, 0));
        var deepInB = ShipAtmosphereZone.FindZoneAt(zoneA.GetWorld3D(), new Vector3(13.5f, 1, 0));

        AssertBool(ReferenceEquals(deepInA, zoneA)).IsTrue();
        AssertBool(ReferenceEquals(deepInB, zoneB)).IsTrue();
    }

    /// <summary>Regression coverage for the airlock bug where standing right at a closed docked
    /// airlock read the far (docked) ship's atmosphere instead of the Home Ship's own: the
    /// previous test above only ever queried points exclusively inside one zone's range (8.5 is
    /// outside zoneB's [10,14], 13.5 is outside zoneA's [8,12]) — it never actually exercised a
    /// point genuinely inside BOTH shapes at once, which is exactly what a real docked airlock
    /// threshold produces (see ShipAtmosphereZone.ContainmentMargin's own doc comment for the
    /// real-scene numbers). x=10.5 sits inside both zoneA's [8,12] and zoneB's [10,14] — margin
    /// 1.5 into zoneA (center 10) vs only 0.5 into zoneB (center 12), so the more-centrally-
    /// contained zone (A) must win, not whichever IntersectPoint enumerates first.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task FindZoneAt_WhenGenuinelyInsideBothShapes_PicksTheMoreCentrallyContainedZone()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var zoneA = AutoFree(MakeZoneWithShape(
            sceneTree, shipA, new Vector2I(1, 1), new Vector3(10, 1, 0), new Vector3(4, 2, 2))); // spans x[8,12]
        var zoneB = AutoFree(MakeZoneWithShape(
            sceneTree, shipB, new Vector2I(2, 2), new Vector3(12, 1, 0), new Vector3(4, 2, 2))); // spans x[10,14]

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var inOverlap = ShipAtmosphereZone.FindZoneAt(zoneA.GetWorld3D(), new Vector3(10.5f, 1, 0));

        AssertBool(ReferenceEquals(inOverlap, zoneA)).IsTrue();
    }

    /// <summary>Regression coverage for a real bug this project hit: a docked Derelict's own room
    /// zone (ShipZoneWideShape, 10 units wide) sitting right next to its docking corridor spans
    /// far enough past its own hull to reach clear across the docking seam into the Home Ship's
    /// own (much smaller, ~2.2-unit) corridor zone — and an un-normalized (raw world-unit)
    /// containment margin is systematically biased toward whichever shape is physically bigger,
    /// so the small, correct zone lost almost everywhere except right at the very edge. Mirrors
    /// the real sizes/positions: a small zone (matching the Home Ship's corridor) at world x=10,
    /// half-width 1.1, vs a big zone (matching the Derelict's room) at world x=14, half-width 5 —
    /// their spans overlap over x∈[9,11.1]. A point deep inside the SMALL zone's own span, but
    /// only shallowly not-quite-central for the BIG zone, must still resolve to the small zone.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task FindZoneAt_WhenASmallZoneOverlapsAMuchBiggerOne_StillPicksTheSmallZoneWhenDeeperInsideIt()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var smallShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(smallShip);
        var bigShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(bigShip);

        var smallZone = AutoFree(MakeZoneWithShape(
            sceneTree, smallShip, new Vector2I(1, 1), new Vector3(10, 1, 0), new Vector3(2.2f, 2.2f, 2.4f))); // spans x[8.9,11.1]
        var bigZone = AutoFree(MakeZoneWithShape(
            sceneTree, bigShip, new Vector2I(2, 2), new Vector3(14, 1, 0), new Vector3(10, 2.2f, 6.4f))); // spans x[9,19]

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        // 9.1: near the small zone's own edge (10% of its own half-width in) but very shallow
        // (2%) for the big zone — small zone should still win, being relatively deeper inside it.
        var nearSmallZonesOwnEdge = ShipAtmosphereZone.FindZoneAt(smallZone.GetWorld3D(), new Vector3(9.1f, 1, 0));
        AssertBool(ReferenceEquals(nearSmallZonesOwnEdge, smallZone)).IsTrue();

        // 10.5: about halfway across the small zone's own span, still clearly inside it.
        var midSmallZone = ShipAtmosphereZone.FindZoneAt(smallZone.GetWorld3D(), new Vector3(10.5f, 1, 0));
        AssertBool(ReferenceEquals(midSmallZone, smallZone)).IsTrue();
    }

    /// <summary>Regression coverage for a second real bug found debugging the same airlock issue:
    /// the very first version of the size-normalized tie-break above still failed in the real
    /// game, because it compared ALL THREE axes (X/Y/Z) — and every room-type zone in this game
    /// shares roughly the same vertical (Y) span (floor-to-ceiling, centered around Y≈1), so a
    /// real player position near the floor (Y≈0) sits far enough from that shared center that Y
    /// becomes the smallest (binding) margin for EVERY candidate almost equally, collapsing the
    /// tie-break back to arbitrary IntersectPoint order. Confirmed via real in-game debug
    /// logging: three overlapping zones all reported the identical 0.092 margin at once. Only the
    /// horizontal (X/Z) axes should ever decide "which room" — this test queries at Y=0 (floor
    /// level) against zones centered at Y=1, deliberately NOT aligned with the zone's own
    /// vertical center, which is exactly what the previous "same zone sizes" tests above never
    /// exercised (they queried at Y=1, matching every zone's own center perfectly).</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task FindZoneAt_QueriedNearTheFloor_StillPicksByHorizontalContainment_NotVerticalOffset()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var smallShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(smallShip);
        var bigShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(bigShip);

        var smallZone = AutoFree(MakeZoneWithShape(
            sceneTree, smallShip, new Vector2I(1, 1), new Vector3(10, 1, 0), new Vector3(2.2f, 2.2f, 2.4f))); // spans x[8.9,11.1]
        var bigZone = AutoFree(MakeZoneWithShape(
            sceneTree, bigShip, new Vector2I(2, 2), new Vector3(14, 1, 0), new Vector3(10, 2.2f, 6.4f))); // spans x[9,19]

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        // Floor-level query (Y≈0), far from both zones' shared Y=1 center — must still resolve
        // to the small zone, matching the real reported bug's exact position.
        var atFloorLevel = ShipAtmosphereZone.FindZoneAt(smallZone.GetWorld3D(), new Vector3(9.49f, 0.001f, 0.03f));
        AssertBool(ReferenceEquals(atFloorLevel, smallZone)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TileAt_ConvertsWorldPositionToTileUsingTheParentShipRootsLocalSpace()
    {
        // A zone's parent is always its ship's spatial root (HomeShip/Derelict/Station) — here
        // offset from world origin, matching how a real ship (e.g. the Derelict) sits at a
        // non-zero world transform. TileAt must convert relative to that root, not world space
        // directly.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D { Transform = new Transform3D(Basis.Identity, new Vector3(16, 0, 0)) });
        sceneTree.Root.AddChild(shipRoot);

        var zone = AutoFree(new ShipAtmosphereZone());
        shipRoot.AddChild(zone);

        // Same "+3, floor" local-space convention as ShipBuildTarget's own aim-point conversion:
        // local grid-space X=9.5 -> tile 12, local Z=2.5 -> tile 5.
        var worldPoint = shipRoot.ToGlobal(new Vector3(9.5f, 0, 2.5f));

        var tile = zone.TileAt(worldPoint);

        AssertBool(tile == new Vector2I(12, 5)).IsTrue();
    }
}
