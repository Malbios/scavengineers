using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for PickupItem/ContainerPickupItem's one-time startup freeze:
/// both start frozen (dodging the floor-collision-generation race on the very first frame — see
/// PickupItem's own doc comment) and must unfreeze on their own first physics tick regardless of
/// room/vacuum state, since gravity itself is handled separately by ShipAtmosphereZone's Area3D
/// override — a loose item should be a live, pushable physics body under normal gravity too, not
/// just while drifting in a breach.</summary>
[TestSuite]
public class PickupItemTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void PickupItem_UnfreezesAfterFirstPhysicsTick_RegardlessOfRoomVacuumState()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var item = AutoFree(new PickupItem());
        sceneTree.Root.AddChild(item); // triggers _Ready(): Freeze = true

        AssertBool(item.Freeze).IsTrue(); // sanity: starts frozen

        item._PhysicsProcess(0); // simulate the first physics tick

        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_ReflectsChargeAsDurability_ForADurableToolSittingLoose()
    {
        var item = AutoFree(new PickupItem { ItemId = "crowbar", Charge = 0.6f });

        AssertFloat(item.Condition!.Value).IsEqual(0.6f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Condition_IsNull_ForAnItemThatIsNotADurableTool()
    {
        var item = AutoFree(new PickupItem { ItemId = "scrap_metal", Charge = 0.6f });

        AssertObject(item.Condition).IsNull();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ContainerPickupItem_UnfreezesAfterFirstPhysicsTick_RegardlessOfRoomVacuumState()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var item = AutoFree(new ContainerPickupItem());
        sceneTree.Root.AddChild(item); // triggers _Ready(): Freeze = true

        AssertBool(item.Freeze).IsTrue(); // sanity: starts frozen

        item._PhysicsProcess(0); // simulate the first physics tick

        AssertBool(item.Freeze).IsFalse();
    }

    /// <summary>A vented, breached room (mirrors Player.cs's own decompression-pull hazard setup)
    /// — one hull breach at (0,0), vented for a single 1s tick so O2Fraction/Pressure land at
    /// AtmosphereSystem's own documented ~0.67% remaining (well below ZeroGO2Threshold, but a
    /// small nonzero pullStrength rather than a float-underflowed zero from over-venting). The
    /// zone's CollisionShape3D is a generous box so FindZoneAt can see a test item placed either
    /// right at the breach or a good distance above it (TileAt only reads X/Z, so moving along Y
    /// alone changes distance-to-breach without ever leaving cell (0,0)'s own room/pressure
    /// reading).</summary>
    private static (ShipSim ShipSim, ShipBuildTarget BuildTarget, ShipAtmosphereZone Zone) MakeBreachedRoom(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var cell = new CellCoord(0, 0);
        shipSim.Deck.BreachHull(cell, StructuralSurface.Floor);

        // Small per-tick dt (a real 60Hz physics step), not one big Tick(1) — Vent's own Lerp
        // factor is Clamp(VentRatePerSecond * dt, 0, 1), so a single dt=1 call clamps straight to
        // 1 (an instant, exact jump to Vacuum) rather than the gradual approach a real frame rate
        // produces. 60 steps of dt=1/60 lands just under ZeroGO2Threshold with a small but clearly
        // nonzero Pressure/O2Fraction remaining — enough to exercise a real (nonzero) pullStrength
        // rather than either "still fully breathable" or "exactly vacuum, zero pull" edge case.
        for (var i = 0; i < 60; i++)
        {
            shipSim.Atmosphere!.Tick(1.0 / 60.0);
        }

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        var zone = new ShipAtmosphereZone
        {
            ShipSimRef = shipSim,
            Tile = new Vector2I(0, 0),
            BuildTargetRef = buildTarget,
        };
        shipRoot.AddChild(zone);
        zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 20, 20) } });

        return (shipSim, buildTarget, zone);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PickupItem_OutsidePullRange_IsUnaffectedByTheBreach()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new PickupItem());
        zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(0, 10, 0); // same cell, far above

        item._PhysicsProcess(0); // settle
        item._PhysicsProcess(1); // breach check, 1s delta

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PickupItem_WithinPullRange_GetsPulledTowardTheBreach()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new PickupItem());
        zone.AddChild(item);
        var start = breachPosition + new Vector3(0, 2, 0); // within DecompressionPullRange (5)
        item.GlobalPosition = start;

        item._PhysicsProcess(0); // settle
        item._PhysicsProcess(1); // breach check, 1s delta

        AssertBool(item.LinearVelocity != Vector3.Zero).IsTrue();
        var towardBreach = (breachPosition - start).Normalized();
        AssertBool(item.LinearVelocity.Normalized().Dot(towardBreach) > 0.99f).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PickupItem_WithinEjectDistance_FreezesJustPastTheBreachAndStaysThatWay()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new PickupItem());
        zone.AddChild(item);
        var start = breachPosition + new Vector3(0, 0.1f, 0); // within BreachEjectDistance (0.3)
        item.GlobalPosition = start;

        item._PhysicsProcess(0); // settle
        item._PhysicsProcess(1); // breach check — should eject

        AssertBool(item.Freeze).IsTrue();
        // Item starts above the breach, so toBreach (breachPosition - start) points down — Eject
        // continues that same direction past the breach, i.e. downward, out through the floor.
        var expected = breachPosition + new Vector3(0, -1, 0); // BreachEjectOffset, same direction
        AssertBool(item.GlobalPosition.DistanceTo(expected) < 0.01f).IsTrue();
        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();

        var positionBefore = item.GlobalPosition;
        item._PhysicsProcess(1); // should be fully inert now

        AssertBool(item.GlobalPosition == positionBefore).IsTrue();
        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ContainerPickupItem_OutsidePullRange_IsUnaffectedByTheBreach()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new ContainerPickupItem());
        zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(0, 10, 0);

        item._PhysicsProcess(0);
        item._PhysicsProcess(1);

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ContainerPickupItem_WithinPullRange_GetsPulledTowardTheBreach()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new ContainerPickupItem());
        zone.AddChild(item);
        var start = breachPosition + new Vector3(0, 2, 0);
        item.GlobalPosition = start;

        item._PhysicsProcess(0);
        item._PhysicsProcess(1);

        AssertBool(item.LinearVelocity != Vector3.Zero).IsTrue();
        var towardBreach = (breachPosition - start).Normalized();
        AssertBool(item.LinearVelocity.Normalized().Dot(towardBreach) > 0.99f).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ContainerPickupItem_WithinEjectDistance_FreezesJustPastTheBreachAndStaysThatWay()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone) = MakeBreachedRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new ContainerPickupItem());
        zone.AddChild(item);
        var start = breachPosition + new Vector3(0, 0.1f, 0);
        item.GlobalPosition = start;

        item._PhysicsProcess(0);
        item._PhysicsProcess(1);

        AssertBool(item.Freeze).IsTrue();
        // See PickupItem's own test for why this is -1, not +1, in Y.
        var expected = breachPosition + new Vector3(0, -1, 0);
        AssertBool(item.GlobalPosition.DistanceTo(expected) < 0.01f).IsTrue();
        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();

        var positionBefore = item.GlobalPosition;
        item._PhysicsProcess(1);

        AssertBool(item.GlobalPosition == positionBefore).IsTrue();
        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsTrue();
    }

    /// <summary>Regression coverage for the "sealed room still feels a distant breach through a
    /// closed door" bug: cell0 (the "first room") had its own breach patched and is now sealed
    /// off from cell1 (a closed door edge) — cell1 still has its own active breach. A ship with no
    /// life support (default) never regenerates air, so cell0's O2Fraction stays at "reads as
    /// vacuum" forever even though it's properly sealed and disconnected now — inZeroG alone
    /// can't tell these apart, so the fix adds a real IsConnectedToOutside check.</summary>
    private static (ShipSim ShipSim, ShipBuildTarget BuildTarget, ShipAtmosphereZone Zone, CellCoord Cell0, CellCoord Cell1) MakeSealedRoomBehindAPatchedBreach(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var cell0 = new CellCoord(0, 0);
        var cell1 = new CellCoord(1, 0);

        shipSim.Deck.BreachHull(cell0, StructuralSurface.Floor);
        for (var i = 0; i < 60; i++)
        {
            shipSim.Atmosphere!.Tick(1.0 / 60.0);
        }
        shipSim.Deck.RepairHull(cell0, StructuralSurface.Floor);
        shipSim.Deck.SealEdge(cell0, cell1); // the closed door
        // cell0 sits at a grid corner, so (1,0) and (0,1) are its only two in-grid neighbors
        // (the other two directions are off-grid, already excluded automatically). Sealing just
        // the door edge above isn't enough on its own — cell0 could still reach cell1 (and thus
        // Outside) the long way around via (0,1) and the rest of the open floor plan. Sealing
        // this second edge too makes cell0 a fully isolated one-cell "sealed room, one door,
        // currently closed" — the actual topology this bug needs to reproduce.
        shipSim.Deck.SealEdge(cell0, new CellCoord(cell0.X, cell0.Y + 1));

        shipSim.Deck.BreachHull(cell1, StructuralSurface.Floor);
        for (var i = 0; i < 60; i++)
        {
            shipSim.Atmosphere!.Tick(1.0 / 60.0);
        }

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        var zone = new ShipAtmosphereZone
        {
            ShipSimRef = shipSim,
            Tile = new Vector2I(cell0.X, cell0.Y),
            BuildTargetRef = buildTarget,
        };
        shipRoot.AddChild(zone);
        zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 20, 20) } });

        return (shipSim, buildTarget, zone, cell0, cell1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PickupItem_InASealedRoomBehindAPatchedBreach_IgnoresADistantBreachThroughAClosedDoor()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (shipSim, buildTarget, zone, cell0, cell1) = MakeSealedRoomBehindAPatchedBreach(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(shipSim.Atmosphere!.IsConnectedToOutside(cell0)).IsFalse(); // sanity: sealed, patched
        AssertBool(shipSim.Atmosphere!.IsConnectedToOutside(cell1)).IsTrue(); // sanity: still breached

        var breachPosition = buildTarget.ActiveBreachPositions().Single(); // only cell1's remains
        var item = AutoFree(new PickupItem());
        zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(-1, 0, 0); // sitting at cell0, ~1m away

        item._PhysicsProcess(0); // settle
        item._PhysicsProcess(1); // breach check — must NOT pull, sealed off from that breach

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ContainerPickupItem_InASealedRoomBehindAPatchedBreach_IgnoresADistantBreachThroughAClosedDoor()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, zone, _, _) = MakeSealedRoomBehindAPatchedBreach(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new ContainerPickupItem());
        zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(-1, 0, 0);

        item._PhysicsProcess(0);
        item._PhysicsProcess(1);

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }

    /// <summary>Regression coverage for "sucked toward a breach through the dividing wall instead
    /// of through the doorway": cell0 (Room1) and cell1 (Room2) are joined by an UNSEALED edge (an
    /// open door), and cell1 has its own active breach — so cell0 IS legitimately connected to
    /// Outside via the open door (unlike the sealed-room test above). But the breach itself is
    /// physically in Room2's own zone, not Room1's, so a straight-line pull from Room1 toward it
    /// would cut through the dividing wall. Two small, non-overlapping zone shapes (one per cell)
    /// stand in for the two separate rooms.</summary>
    private static (ShipSim ShipSim, ShipBuildTarget BuildTarget, ShipAtmosphereZone Room1Zone, CellCoord Cell0) MakeTwoRoomsJoinedByAnOpenDoorWithABreachInTheFarRoom(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var cell0 = new CellCoord(0, 0); // Room1
        var cell1 = new CellCoord(1, 0); // Room2 — edge to cell0 left unsealed (open door)

        shipSim.Deck.BreachHull(cell1, StructuralSurface.Floor);
        for (var i = 0; i < 60; i++)
        {
            shipSim.Atmosphere!.Tick(1.0 / 60.0);
        }

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        var room1Zone = new ShipAtmosphereZone
        {
            ShipSimRef = shipSim,
            Tile = new Vector2I(cell0.X, cell0.Y),
            BuildTargetRef = buildTarget,
            Transform = new Transform3D(Basis.Identity, new Vector3(-2.5f, 0, -2.5f)), // cell0's own tile center
        };
        shipRoot.AddChild(room1Zone);
        room1Zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 4, 0.8f) } });

        var room2Zone = new ShipAtmosphereZone
        {
            ShipSimRef = shipSim,
            Tile = new Vector2I(cell1.X, cell1.Y),
            BuildTargetRef = buildTarget,
            Transform = new Transform3D(Basis.Identity, new Vector3(-1.5f, 0, -2.5f)), // cell1's own tile center
        };
        shipRoot.AddChild(room2Zone);
        room2Zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 4, 0.8f) } });

        return (shipSim, buildTarget, room1Zone, cell0);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PickupItem_InARoomConnectedByAnOpenDoor_IgnoresABreachPhysicallyInTheOtherRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (shipSim, buildTarget, room1Zone, cell0) = MakeTwoRoomsJoinedByAnOpenDoorWithABreachInTheFarRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(shipSim.Atmosphere!.IsConnectedToOutside(cell0)).IsTrue(); // sanity: genuinely connected via the open door

        var breachPosition = buildTarget.ActiveBreachPositions().Single(); // cell1's, in Room2
        var item = AutoFree(new PickupItem());
        room1Zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(-1, 0, 0); // sitting at cell0, in Room1

        item._PhysicsProcess(0); // settle
        item._PhysicsProcess(1); // breach check — connected via the open door, but wrong room

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ContainerPickupItem_InARoomConnectedByAnOpenDoor_IgnoresABreachPhysicallyInTheOtherRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, buildTarget, room1Zone, _) = MakeTwoRoomsJoinedByAnOpenDoorWithABreachInTheFarRoom(sceneTree);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var breachPosition = buildTarget.ActiveBreachPositions().Single();
        var item = AutoFree(new ContainerPickupItem());
        room1Zone.AddChild(item);
        item.GlobalPosition = breachPosition + new Vector3(-1, 0, 0);

        item._PhysicsProcess(0);
        item._PhysicsProcess(1);

        AssertBool(item.LinearVelocity == Vector3.Zero).IsTrue();
        AssertBool(item.Freeze).IsFalse();
    }
}
