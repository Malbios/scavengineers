using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for TravelConsoleVerbTarget's generalized N-destination model
/// (Station + parallel Derelict NodePath arrays, resolved via GetNode in _Ready) replacing the
/// old binary Location enum.</summary>
[TestSuite]
public class TravelConsoleVerbTargetTest
{
    private static (TravelConsoleVerbTarget Console, Node3D StationGroup, Node3D[] DerelictGroups) CreateConsole(SceneTree sceneTree, int derelictCount, bool homeShipHasPowerGrid = false)
    {
        // HasPowerGrid must be set before the node enters the tree (see
        // ShipBuildTargetFixtureUpkeepTest.MakePoweredHarness) — defaults false so the other,
        // non-thruster test cases below stay on the plain bare-ShipSim harness they already use.
        var homeShip = AutoFree(new ShipSim { HasPowerGrid = homeShipHasPowerGrid });
        sceneTree.Root.AddChild(homeShip);

        var stationShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(stationShip);

        var stationGroup = AutoFree(new Node3D { Name = "StationGroup" });
        sceneTree.Root.AddChild(stationGroup);

        var stationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "StationAirlock", ShipARef = homeShip, ShipBRef = stationShip });
        sceneTree.Root.AddChild(stationAirlock);

        var derelictGroups = new Node3D[derelictCount];
        var derelictGroupPaths = new Godot.Collections.Array<NodePath>();
        var derelictShipSimPaths = new Godot.Collections.Array<NodePath>();
        var derelictMapPositions = new Godot.Collections.Array<Vector2>();
        ShipSim? firstDerelictShip = null;

        for (var i = 0; i < derelictCount; i++)
        {
            var group = AutoFree(new Node3D { Name = $"DerelictGroup{i + 1}" });
            sceneTree.Root.AddChild(group);
            derelictGroups[i] = group;

            var derelictShip = new ShipSim { Name = "ShipSim" };
            group.AddChild(derelictShip);
            firstDerelictShip ??= derelictShip;

            derelictGroupPaths.Add(new NodePath($"../DerelictGroup{i + 1}"));
            derelictShipSimPaths.Add(new NodePath($"../DerelictGroup{i + 1}/ShipSim"));
            derelictMapPositions.Add(new Vector2(i * 10, i * 10));
        }

        var derelictAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "DerelictAirlock", ShipARef = homeShip, ShipBRef = firstDerelictShip });
        sceneTree.Root.AddChild(derelictAirlock);

        var console = AutoFree(new TravelConsoleVerbTarget
        {
            ShipSimRef = homeShip,
            StationAirlock = stationAirlock,
            DerelictAirlock = derelictAirlock,
            StationGroup = stationGroup,
            DerelictGroupPaths = derelictGroupPaths,
            DerelictShipSimPaths = derelictShipSimPaths,
            DerelictMapPositions = derelictMapPositions,
        });
        sceneTree.Root.AddChild(console);

        return (console, stationGroup, derelictGroups);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_ToADerelict_SetsCurrentDestinationAndOnlyThatGroupVisible()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationGroup, derelictGroups) = CreateConsole(sceneTree, 3);

        console.ApplySaveState("derelict_2");

        AssertBool(console.CurrentDestinationId == 2).IsTrue();
        AssertBool(stationGroup.Visible).IsFalse();
        AssertBool(derelictGroups[0].Visible).IsFalse();
        AssertBool(derelictGroups[1].Visible).IsTrue();
        AssertBool(derelictGroups[2].Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_LegacyBareDerelict_MapsToDerelict1()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 2);

        console.ApplySaveState("derelict");

        AssertBool(console.CurrentDestinationId == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_UnrecognizedValue_FallsBackToStation()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 2);
        console.ApplySaveState("derelict_2"); // start somewhere other than Station

        console.ApplySaveState("bogus");

        AssertBool(console.CurrentDestinationId == 0).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GetSaveState_RoundTripsForStationAndEveryDerelict()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 2);

        console.ApplySaveState("station");
        AssertBool(console.GetSaveState() == "station").IsTrue();

        console.ApplySaveState("derelict_1");
        AssertBool(console.GetSaveState() == "derelict_1").IsTrue();

        console.ApplySaveState("derelict_2");
        AssertBool(console.GetSaveState() == "derelict_2").IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BeginTravel_ToAlreadyCurrentDestination_IsNoOp()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 2);

        console.BeginTravel(0); // already at Station (the default)

        AssertBool(console.CurrentVerbProgress is null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BeginTravel_ToADifferentDestination_StartsTraveling()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 2);

        console.BeginTravel(1);

        AssertBool(console.CurrentVerbProgress is not null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task BeginTravel_WithFueledThrusters_ProgressesFasterThanWithNone()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bareConsole, _, _) = CreateConsole(sceneTree, 1);
        var (fueledConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        // A chain of Manhattan-adjacent fixtures back to the battery — no conduit needed, the
        // same direct-adjacency shortcut Battery+Switch's own default placement already relies
        // on (see ShipBuildTarget.BatteryEdge/SwitchEdge's doc comment).
        fueledConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        fueledConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        fueledConsole.ShipSimRef!.InstallThruster("t2", new CellCoord(2, 0), FixtureSurface.WallInner);
        fueledConsole.ShipSimRef!.InstallThruster("t3", new CellCoord(3, 0), FixtureSurface.WallInner);

        bareConsole.BeginTravel(1);
        fueledConsole.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        // Same elapsed real time, but the fueled console's own trip is much shorter (3 fueled
        // thrusters hits MinTravelSeconds via BaseTravelSeconds - 3*ReductionPerThruster), so its
        // progress fraction must be further along than the unfueled console's own still-mostly-
        // BaseTravelSeconds-long trip.
        AssertFloat(fueledConsole.CurrentVerbProgress!.Value).IsGreater(bareConsole.CurrentVerbProgress!.Value);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task BeginTravel_ThrustersAtZeroCharge_DoNotCountTowardTheSpeedBonus()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (bareConsole, _, _) = CreateConsole(sceneTree, 1);
        var (emptyThrusterConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        // Powered (adjacent to a real battery) so charge is the only variable under test — not
        // an incidental "unpowered" exclusion masquerading as the zero-charge one.
        emptyThrusterConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        emptyThrusterConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        emptyThrusterConsole.ShipSimRef!.SetThrusterCharge("t1", 0f);

        bareConsole.BeginTravel(1);
        emptyThrusterConsole.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(emptyThrusterConsole.CurrentVerbProgress!.Value).IsEqual(bareConsole.CurrentVerbProgress!.Value);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Traveling_DrainsEveryFueledThrustersN2_ButLeavesAnAlreadyEmptyOneAtZero()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("fueled", new CellCoord(1, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("empty", new CellCoord(2, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("empty", 0f);

        console.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("fueled")).IsLess(1f);
        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("empty")).IsEqual(0f);
    }
}
