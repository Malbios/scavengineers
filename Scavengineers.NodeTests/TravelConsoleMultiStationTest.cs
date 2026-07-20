using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for generalizing TravelConsoleVerbTarget's Station handling from
/// a single hardcoded destination (id 0) into a parallel-array model matching Derelict's own
/// shape, plus a fourth array since every Station needs its own dedicated, never-shared airlock
/// (unlike Derelicts, which all share one rebindable one — see AirlockDoorVerbTarget.RebindFarSide
/// and TravelConsoleVerbTarget's own class doc comment). Destination ids are now
/// 0..StationCount-1 = Station N, StationCount.. = Derelict N.</summary>
[TestSuite]
public class TravelConsoleMultiStationTest
{
    private static (TravelConsoleVerbTarget Console, AirlockDoorVerbTarget[] StationAirlocks, AirlockDoorVerbTarget DerelictAirlock) MakeHarness(SceneTree sceneTree)
    {
        var homeShip = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(homeShip);

        var stationGroupPaths = new Godot.Collections.Array<NodePath>();
        var stationAirlockPaths = new Godot.Collections.Array<NodePath>();
        var stationMapPositions = new Godot.Collections.Array<Vector2>();
        var stationAirlocks = new AirlockDoorVerbTarget[2];

        for (var i = 0; i < 2; i++)
        {
            var stationShip = AutoFree(new ShipSim());
            sceneTree.Root.AddChild(stationShip);

            var stationGroup = AutoFree(new Node3D { Name = $"StationGroup{i}" });
            sceneTree.Root.AddChild(stationGroup);

            var stationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = $"StationAirlock{i}", ShipARef = homeShip, ShipBRef = stationShip });
            sceneTree.Root.AddChild(stationAirlock);
            stationAirlocks[i] = stationAirlock;

            stationGroupPaths.Add(new NodePath($"../StationGroup{i}"));
            stationAirlockPaths.Add(new NodePath($"../StationAirlock{i}"));
            stationMapPositions.Add(new Vector2(i * 100, i * 100));
        }

        var derelictGroup = AutoFree(new Node3D { Name = "DerelictGroup1" });
        sceneTree.Root.AddChild(derelictGroup);

        var derelictShip = new ShipSim { Name = "ShipSim" };
        derelictGroup.AddChild(derelictShip);

        var derelictAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "DerelictAirlock", ShipARef = homeShip, ShipBRef = derelictShip });
        sceneTree.Root.AddChild(derelictAirlock);

        var console = AutoFree(new TravelConsoleVerbTarget
        {
            ShipSimRef = homeShip,
            DerelictAirlock = derelictAirlock,
            StationGroupPaths = stationGroupPaths,
            StationAirlockPaths = stationAirlockPaths,
            StationMapPositions = stationMapPositions,
            DerelictGroupPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1") },
            DerelictShipSimPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1/ShipSim") },
            DerelictMapPositions = new Godot.Collections.Array<Vector2> { new(10, 10) },
            BaseTravelSeconds = 0.3f,
            MinTravelSeconds = 0.1f,
        });
        sceneTree.Root.AddChild(console);

        homeShip.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        homeShip.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        homeShip.SetThrusterCharge("t1", 1f);

        return (console, stationAirlocks, derelictAirlock);
    }

    private static async Task TravelAndDockAsync(SceneTree sceneTree, TravelConsoleVerbTarget console, int destinationId)
    {
        console.BeginTravel(destinationId);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        console.CompleteDocking();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildMapEntries_ListsBothStationsFirstThenTheDerelict_WithIdsShiftedPastStationCount()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = MakeHarness(sceneTree);

        var entries = console.BuildMapEntries();

        AssertInt(entries.Count).IsEqual(3);
        AssertInt(entries[0].DestinationId).IsEqual(0);
        AssertBool(entries[0].DisplayNameKey == "OBJECT_STATION").IsTrue();
        AssertInt(entries[1].DestinationId).IsEqual(1);
        AssertBool(entries[1].DisplayNameKey == "OBJECT_STATION_2").IsTrue();
        AssertInt(entries[2].DestinationId).IsEqual(2);
        AssertBool(entries[2].DisplayNameKey == "OBJECT_DERELICT_1").IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelingToTheSecondStation_DocksOnlyItsOwnAirlock()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationAirlocks, derelictAirlock) = MakeHarness(sceneTree);

        await TravelAndDockAsync(sceneTree, console, 1);

        AssertInt(console.CurrentDestinationId).IsEqual(1);
        AssertBool(stationAirlocks[0].Docked).IsFalse();
        AssertBool(stationAirlocks[1].Docked).IsTrue();
        AssertBool(derelictAirlock.Docked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelingToTheDerelict_UndocksBothStationAirlocks_AndDocksTheDerelictOne()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationAirlocks, derelictAirlock) = MakeHarness(sceneTree);

        // Destination 2 = the Derelict, since StationCount is 2 here (ids 0/1 are the Stations).
        await TravelAndDockAsync(sceneTree, console, 2);

        AssertInt(console.CurrentDestinationId).IsEqual(2);
        AssertBool(stationAirlocks[0].Docked).IsFalse();
        AssertBool(stationAirlocks[1].Docked).IsFalse();
        AssertBool(derelictAirlock.Docked).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task GetSaveState_RoundTrips_ForTheSecondStation()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _) = MakeHarness(sceneTree);

        await TravelAndDockAsync(sceneTree, console, 1);
        var saved = console.GetSaveState();
        AssertBool(saved == "station_1").IsTrue();

        console.ApplySaveState(saved);
        AssertInt(console.CurrentDestinationId).IsEqual(1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_LegacyBareStation_ResolvesToStationZero()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationAirlocks, _) = MakeHarness(sceneTree);

        console.ApplySaveState("station");

        AssertInt(console.CurrentDestinationId).IsEqual(0);
        AssertBool(stationAirlocks[0].Docked).IsTrue();
        AssertBool(stationAirlocks[1].Docked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_LegacyBareDerelict_ResolvesToTheFirstDerelict_ShiftedPastBothStations()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, derelictAirlock) = MakeHarness(sceneTree);

        console.ApplySaveState("derelict");

        // StationCount (2) here, not 1 — the bare-derelict legacy fallback must still land past
        // every Station, however many of them exist.
        AssertInt(console.CurrentDestinationId).IsEqual(2);
        AssertBool(derelictAirlock.Docked).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_ExistingDerelictString_StillResolvesCorrectly_WithTwoStations()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, derelictAirlock) = MakeHarness(sceneTree);

        console.ApplySaveState("derelict_1");

        AssertInt(console.CurrentDestinationId).IsEqual(2);
        AssertBool(derelictAirlock.Docked).IsTrue();
        AssertBool(console.GetSaveState() == "derelict_1").IsTrue();
    }
}
