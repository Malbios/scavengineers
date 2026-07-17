using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for a scene-tree/deferred-call ordering bug: on a fresh game (no
/// save loaded), TravelConsoleVerbTarget's own initial CallDeferred(ApplyCurrentLocation) — which
/// hides and decollides every non-current Derelict via SetShipPresence — used to run BEFORE each
/// Derelict's own ShipBuildTarget.SeedDefaultShipLayout (also deferred, from its own _Ready) had
/// generated its wall/conduit/machine colliders. SetShipPresence can only disable colliders that
/// already exist at the moment it runs, so those walls stayed permanently solid despite the group
/// correctly reading as invisible (Visible propagates live; Disabled does not retroactively
/// apply). World.tscn lists HomeShip (containing TravelConsoleVerbTarget) as an earlier sibling
/// than Derelict1..5, so this harness deliberately adds the console before the derelict group to
/// reproduce that same ordering.</summary>
[TestSuite]
public class TravelConsoleDerelictPresenceRaceTest
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task NonCurrentDerelictsWallColliders_EndUpDisabled_DespiteBeingGeneratedAfterTheInitialPresenceSweep()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();

        // A real packed scene builds its whole node tree first, THEN fires _Ready() on every
        // node (deepest-first, in sibling declaration order) — the structure already fully
        // exists by the time any _Ready() runs, only the CALL order is sequential. Assembling
        // everything under this detached root first, and adding it to the live tree as a single
        // atomic operation at the end, reproduces that — building nodes directly under
        // sceneTree.Root one AddChild at a time (each of which fires _Ready() immediately, since
        // that tree is already running) would NOT: a NodePath like "../DerelictGroup1" would fail
        // to resolve for whichever sibling hasn't been added yet.
        var worldRoot = AutoFree(new Node3D { Name = "World" });

        var homeShip = new ShipSim();
        worldRoot.AddChild(homeShip);

        var stationShip = new ShipSim();
        worldRoot.AddChild(stationShip);

        var stationGroup = new Node3D { Name = "StationGroup" };
        worldRoot.AddChild(stationGroup);

        var stationAirlock = new AirlockDoorVerbTarget { Name = "StationAirlock", ShipARef = homeShip, ShipBRef = stationShip };
        worldRoot.AddChild(stationAirlock);

        // Real corridor cells (EastCorridorLength) so SeedDefaultShipLayout actually has wall
        // segments to spawn — that's the exact geometry the reported bug blocked movement on.
        var derelictShip = new ShipSim { Name = "ShipSim", EastCorridorLength = 2 };

        var derelictGroup = new Node3D { Name = "DerelictGroup1" };
        var derelictBuildTarget = new ShipBuildTarget
        {
            Name = "Floor",
            ShipSimRef = derelictShip,
            ShipRoot = derelictGroup,
            SeedDefaultLayout = true,
        };

        var derelictAirlock = new AirlockDoorVerbTarget { Name = "DerelictAirlock", ShipARef = homeShip, ShipBRef = derelictShip };

        var console = new TravelConsoleVerbTarget
        {
            ShipSimRef = homeShip,
            StationAirlock = stationAirlock,
            DerelictAirlock = derelictAirlock,
            StationGroup = stationGroup,
            DerelictGroupPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1") },
            DerelictShipSimPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1/ShipSim") },
            DerelictMapPositions = new Godot.Collections.Array<Vector2> { Vector2.Zero },
        };

        // Ordering matters: console (HomeShip's branch) is added as a sibling BEFORE the derelict
        // group, exactly matching World.tscn's own HomeShip-before-Derelict1..5 sibling order
        // that this bug depended on (_Ready() then fires in that same order once the whole
        // subtree below enters the live tree).
        worldRoot.AddChild(console);
        worldRoot.AddChild(derelictAirlock);

        derelictGroup.AddChild(derelictShip);
        derelictGroup.AddChild(derelictBuildTarget);
        worldRoot.AddChild(derelictGroup);

        sceneTree.Root.AddChild(worldRoot);

        // Let the deferred-call flush (ApplyCurrentLocation, twice-deferred) and every Derelict's
        // own deferred SeedDefaultShipLayout both run — a couple of real frames' margin past the
        // single flush this actually needs.
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

        AssertBool(derelictGroup.Visible).IsFalse();

        var wallColliders = derelictBuildTarget.FindChildren("*", nameof(CollisionShape3D), recursive: true, owned: false)
            .OfType<CollisionShape3D>()
            .ToList();

        AssertBool(wallColliders.Count > 0).IsTrue();
        AssertBool(wallColliders.All(shape => shape.Disabled)).IsTrue();
    }
}
