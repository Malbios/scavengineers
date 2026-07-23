using System.Linq;
using System.Threading.Tasks;

using System.Collections.Generic;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Travel;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for generalizing TravelConsoleVerbTarget's Station handling from
/// a single hardcoded destination (id 0) into a parallel-array model matching Derelict's own
/// shape. Stations use the exact same rebind pattern as Derelict (one shared Home-Ship-side
/// StationAirlock, its far side + partner door repointed via RebindFarSide) plus their own
/// per-station destination-side door (only ever "docked" while that Station is current) — see
/// TravelConsoleVerbTarget's own class doc comment. Destination ids are
/// 0..StationCount-1 = Station N, StationCount.. = Derelict N.</summary>
[TestSuite]
public class TravelConsoleMultiStationTest
{
    /// <summary>The travel map's names and positions come from DestinationCatalog (i.e. from
    /// Data/destinations.json) now, and this project has no res://Data/ of its own — so the real
    /// Load() yields an empty catalog and BuildMapEntries would list nothing. Seeded here to match
    /// the harness's own two-stations-one-derelict shape.
    ///
    /// No [After] reset, and no need for the synthetic-id caution ItemCatalog's own seeding needs:
    /// nothing else in this project reads DestinationCatalog, so a leak into another suite is
    /// inert, and resetting would only make the next reader re-run Load() and re-warn.</summary>
    [Before]
    public void SeedDestinations() => DestinationCatalog.SeedForTests(new List<DestinationCatalog.DestinationDefinition>
    {
        new() { Id = "station_1", Kind = "station", NameKey = "OBJECT_STATION" },
        new() { Id = "station_2", Kind = "station", NameKey = "OBJECT_STATION_2" },
        new() { Id = "derelict_1", Kind = "derelict", NameKey = "OBJECT_DERELICT_1" },
    });

    private static (TravelConsoleVerbTarget Console, AirlockDoorVerbTarget StationAirlock, AirlockDoorVerbTarget[] StationDestinationAirlocks, AirlockDoorVerbTarget DerelictAirlock) MakeHarness(SceneTree sceneTree)
    {
        var homeShip = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(homeShip);

        var stationGroups = new Node3D[2];
        var stationShipSims = new ShipSim[2];
        var stationDestinationAirlocks = new AirlockDoorVerbTarget[2];

        AirlockDoorVerbTarget? stationAirlock = null;

        for (var i = 0; i < 2; i++)
        {
            var stationShip = AutoFree(new ShipSim { Name = $"StationShip{i}" });
            sceneTree.Root.AddChild(stationShip);
            stationShipSims[i] = stationShip;

            var stationGroup = AutoFree(new Node3D { Name = $"StationGroup{i}" });
            sceneTree.Root.AddChild(stationGroup);
            stationGroups[i] = stationGroup;

            var stationDestinationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = $"StationDestinationAirlock{i}", ShipARef = stationShip, OwnsBridge = false });
            sceneTree.Root.AddChild(stationDestinationAirlock);
            stationDestinationAirlocks[i] = stationDestinationAirlock;

            if (i == 0)
            {
                // The one shared Home-Ship-side door — initially bound to Station 0 to match
                // TravelConsoleVerbTarget's own default _currentDestination, same as
                // DerelictAirlock's own harness setup already does for Derelict 1.
                stationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "StationAirlock", ShipARef = homeShip, ShipBRef = stationShip, PartnerDoorRef = stationDestinationAirlock });
                sceneTree.Root.AddChild(stationAirlock);
                stationDestinationAirlock.PartnerDoorRef = stationAirlock; // bidirectional — see AirlockDoorVerbTarget.RefreshBridgeEngagement
            }
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
            StationAirlock = stationAirlock,
            BaseTravelSeconds = 0.3f,
            MinTravelSeconds = 0.1f,
        });
        sceneTree.Root.AddChild(console);

        // Stations first, then derelicts — the registration order IS the destination id order, and
        // DestinationManager registers straight down the catalog for the same reason.
        for (var i = 0; i < 2; i++)
        {
            console.RegisterStation(stationGroups[i], stationShipSims[i], stationDestinationAirlocks[i], buildTarget: null);
        }

        console.RegisterDerelict(derelictGroup, derelictShip, buildTarget: null);

        homeShip.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        homeShip.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        homeShip.SetThrusterCharge("t1", 1f);

        return (console, stationAirlock!, stationDestinationAirlocks, derelictAirlock);
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
        var (console, _, _, _) = MakeHarness(sceneTree);

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
    public async Task TravelingToTheSecondStation_RebindsTheSharedAirlockAndDocksOnlyItsOwnDestinationDoor()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationAirlock, stationDestinationAirlocks, derelictAirlock) = MakeHarness(sceneTree);

        await TravelAndDockAsync(sceneTree, console, 1);

        AssertInt(console.CurrentDestinationId).IsEqual(1);
        AssertBool(stationAirlock.Docked).IsTrue();
        AssertBool(stationAirlock.PartnerDoorRef == stationDestinationAirlocks[1]).IsTrue();
        AssertBool(stationDestinationAirlocks[0].Docked).IsFalse();
        AssertBool(stationDestinationAirlocks[1].Docked).IsTrue();
        AssertBool(derelictAirlock.Docked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelingToTheDerelict_UndocksTheStationAirlocks_AndDocksTheDerelictOne()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, stationAirlock, stationDestinationAirlocks, derelictAirlock) = MakeHarness(sceneTree);

        // Destination 2 = the Derelict, since StationCount is 2 here (ids 0/1 are the Stations).
        await TravelAndDockAsync(sceneTree, console, 2);

        AssertInt(console.CurrentDestinationId).IsEqual(2);
        AssertBool(stationAirlock.Docked).IsFalse();
        AssertBool(stationDestinationAirlocks[0].Docked).IsFalse();
        AssertBool(stationDestinationAirlocks[1].Docked).IsFalse();
        AssertBool(derelictAirlock.Docked).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task GetSaveState_RoundTrips_ForTheSecondStation()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _, _) = MakeHarness(sceneTree);

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
        var (console, stationAirlock, stationDestinationAirlocks, _) = MakeHarness(sceneTree);

        console.ApplySaveState("station");

        AssertInt(console.CurrentDestinationId).IsEqual(0);
        AssertBool(stationAirlock.Docked).IsTrue();
        AssertBool(stationDestinationAirlocks[0].Docked).IsTrue();
        AssertBool(stationDestinationAirlocks[1].Docked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ApplySaveState_LegacyBareDerelict_ResolvesToTheFirstDerelict_ShiftedPastBothStations()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (console, _, _, derelictAirlock) = MakeHarness(sceneTree);

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
        var (console, _, _, derelictAirlock) = MakeHarness(sceneTree);

        console.ApplySaveState("derelict_1");

        AssertInt(console.CurrentDestinationId).IsEqual(2);
        AssertBool(derelictAirlock.Docked).IsTrue();
        AssertBool(console.GetSaveState() == "derelict_1").IsTrue();
    }
}
