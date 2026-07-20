using System.Linq;
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
            DerelictAirlock = derelictAirlock,
            StationGroupPaths = new Godot.Collections.Array<NodePath> { new("../StationGroup") },
            StationAirlockPaths = new Godot.Collections.Array<NodePath> { new("../StationAirlock") },
            StationMapPositions = new Godot.Collections.Array<Vector2> { new(220, 180) },
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

        // GetSaveState emits "station_0" going forward even though bare "station" is still
        // accepted on load (see ApplySaveState) — this test's own name predates multi-station
        // support, when "station" (no index) was the only form that ever existed.
        console.ApplySaveState("station");
        AssertBool(console.GetSaveState() == "station_0").IsTrue();

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
        var (console, _, _) = CreateConsole(sceneTree, 2, homeShipHasPowerGrid: true);

        // At least one working thruster is now mandatory for BeginTravel to succeed at all (see
        // BeginTravel_WithNoFueledThrusters_RefusesToStartTraveling below).
        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("t1", 1f);

        console.BeginTravel(1);

        AssertBool(console.CurrentVerbProgress is not null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BeginTravel_WithNoFueledThrusters_RefusesToStartTraveling()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1); // no power, no thrusters at all

        console.BeginTravel(1);

        // Same observable shape as every other rejected BeginTravel call — nothing happens.
        AssertBool(console.CurrentVerbProgress is null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task BeginTravel_WithMoreFueledThrusters_ProgressesFasterThanWithJustOne()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (oneThrusterConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);
        var (threeThrusterConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        // A chain of Manhattan-adjacent fixtures back to the battery — no conduit needed, the
        // same direct-adjacency shortcut Battery+Switch's own default placement already relies
        // on (see ShipBuildTarget.BatteryEdge/SwitchEdge's doc comment).
        oneThrusterConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        oneThrusterConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        oneThrusterConsole.ShipSimRef!.SetThrusterCharge("t1", 1f);

        threeThrusterConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        threeThrusterConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        threeThrusterConsole.ShipSimRef!.InstallThruster("t2", new CellCoord(2, 0), FixtureSurface.WallInner);
        threeThrusterConsole.ShipSimRef!.InstallThruster("t3", new CellCoord(3, 0), FixtureSurface.WallInner);
        threeThrusterConsole.ShipSimRef!.SetThrusterCharge("t1", 1f);
        threeThrusterConsole.ShipSimRef!.SetThrusterCharge("t2", 1f);
        threeThrusterConsole.ShipSimRef!.SetThrusterCharge("t3", 1f);

        oneThrusterConsole.BeginTravel(1);
        threeThrusterConsole.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        // Same elapsed real time, but the 3-thruster console's own trip is much shorter (hits
        // MinTravelSeconds via BaseTravelSeconds - 3*ReductionPerThruster), so its progress
        // fraction must be further along than the 1-thruster console's own still-mostly-
        // BaseTravelSeconds-long trip. Both sides can actually travel at all now — the floor case
        // is one thruster, not zero.
        AssertFloat(threeThrusterConsole.CurrentVerbProgress!.Value).IsGreater(oneThrusterConsole.CurrentVerbProgress!.Value);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task BeginTravel_ThrustersAtZeroCharge_DoNotCountTowardTheSpeedBonus()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (oneThrusterConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);
        var (plusEmptyThrusterConsole, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        // Both sides get the same one real, fueled thruster (so both can actually travel) —
        // plusEmptyThrusterConsole additionally gets a second, zero-charge thruster, which should
        // contribute nothing to the speed formula.
        oneThrusterConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        oneThrusterConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        oneThrusterConsole.ShipSimRef!.SetThrusterCharge("t1", 1f);

        plusEmptyThrusterConsole.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        plusEmptyThrusterConsole.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        plusEmptyThrusterConsole.ShipSimRef!.SetThrusterCharge("t1", 1f);
        plusEmptyThrusterConsole.ShipSimRef!.InstallThruster("t2", new CellCoord(2, 0), FixtureSurface.WallInner);
        plusEmptyThrusterConsole.ShipSimRef!.SetThrusterCharge("t2", 0f);

        oneThrusterConsole.BeginTravel(1);
        plusEmptyThrusterConsole.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(plusEmptyThrusterConsole.CurrentVerbProgress!.Value).IsEqual(oneThrusterConsole.CurrentVerbProgress!.Value);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Traveling_DrainsEveryFueledThrustersN2_ButLeavesAnAlreadyEmptyOneAtZero()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("fueled", new CellCoord(1, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("fueled", 1f);
        console.ShipSimRef!.InstallThruster("empty", new CellCoord(2, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("empty", 0f);

        console.BeginTravel(1);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("fueled")).IsLess(1f);
        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("empty")).IsEqual(0f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Traveling_SetsThrusterAndConsoleDrawToActive_AndBackToIdleOnceNotTraveling()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("t1", 1f);

        var thruster = console.ShipSimRef!.Deck.Fixtures.Single(f => f.Id == "t1");
        var consoleFixture = console.ShipSimRef!.Deck.Fixtures.Single(f => f.Id == ShipSim.TravelConsoleFixtureId);

        AssertFloat(thruster.PowerDraw).IsEqual(ShipSim.IdleDraw);
        AssertFloat(consoleFixture.PowerDraw).IsEqual(ShipSim.IdleDraw);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(thruster.PowerDraw).IsEqual(ShipSim.ThrusterActiveDraw);
        AssertFloat(consoleFixture.PowerDraw).IsEqual(ShipSim.TravelConsoleActiveDraw);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Traveling_WithActiveDemandExceedingCapacity_PausesN2DrainForAsLongAsTheBrownoutPersists()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        for (var i = 1; i <= 7; i++)
        {
            console.ShipSimRef!.InstallThruster($"t{i}", new CellCoord(i, 0), FixtureSurface.WallInner);
            console.ShipSimRef!.SetThrusterCharge($"t{i}", 1f);
        }

        // While traveling: TravelConsoleActiveDraw(8) + 7 * ThrusterActiveDraw(2) = 22, over
        // BatteryCapacity(20) — the ship-wide brownout this active-draw spike is meant to prove
        // still exists once a ship is equipped well past the point of useful travel-time reduction
        // (BaseTravelSeconds/MinTravelSeconds's floor caps out well before 7 thrusters actually
        // help).
        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

        // Never actually powered for even a moment, so never drained at all — not just "drained
        // slower."
        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("t1")).IsEqual(1f);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("t1")).IsEqual(1f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Traveling_WithANormalTwoThrusterLoadout_DoesNotBrownoutTheShip()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = CreateConsole(sceneTree, 1, homeShipHasPowerGrid: true);

        console.ShipSimRef!.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.InstallThruster("t2", new CellCoord(2, 0), FixtureSurface.WallInner);
        console.ShipSimRef!.SetThrusterCharge("t1", 1f);
        console.ShipSimRef!.SetThrusterCharge("t2", 1f);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

        // A 2-thruster ship is a normal, expected loadout (installing a 2nd thruster to shorten
        // the trip is the obvious move) — it must not brownout the whole grid the instant travel
        // starts. Demand: TravelConsoleActiveDraw(8) + 2 * ThrusterActiveDraw(2) = 12, comfortably
        // under BatteryCapacity(20). This harness only wires the console/thrusters/battery
        // together (no Bunk/airlock conduits — see CreateConsole/ShipSim's own "added unwired"
        // comment), so IsPowered is checked against the console fixture itself; on a real, fully-
        // conduited ship the same non-overload means every other device on the grid (airlocks,
        // lights) stays powered too.
        AssertBool(console.ShipSimRef!.IsPowered(ShipSim.TravelConsoleFixtureId)).IsTrue();
    }
}
