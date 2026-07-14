using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for TravelConsoleVerbTarget's generalized N-destination model
/// (Station + parallel Derelict NodePath arrays, resolved via GetNode in _Ready) replacing the
/// old binary Location enum.</summary>
[TestSuite]
public class TravelConsoleVerbTargetTest
{
    private static (TravelConsoleVerbTarget Console, Node3D StationGroup, Node3D[] DerelictGroups) CreateConsole(SceneTree sceneTree, int derelictCount)
    {
        var homeShip = AutoFree(new ShipSim());
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
}
