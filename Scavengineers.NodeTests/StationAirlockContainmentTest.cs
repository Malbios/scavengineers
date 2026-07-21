using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the two-independent-doors-per-Station-connection fix: the
/// Home Ship's own StationAirlock and a Station's own DestinationAirlock only actually bridge
/// atmosphere when BOTH report open (AirlockDoorVerbTarget.PartnerDoorRef), and each seals a real
/// edge within its own ship's Deck when closed (SealsLocalEdge). This is what stops opening the
/// wrong/non-current door from flooding the whole Home Ship — the originally reported bug, where
/// two always-present adjacent doors shared one unsealed corridor blob. Ticks _PhysicsProcess
/// directly (same synchronous, deterministic style as ShipSimTest's own atmosphere coverage)
/// rather than awaiting real engine frames, since AirlockBridge's own self-sustaining
/// MarkExternallyVented design (see its doc comment) converges correctly regardless of per-call
/// ordering.</summary>
[TestSuite]
public class StationAirlockContainmentTest
{
    private static (ShipSim HomeShip, ShipSim Station, AirlockDoorVerbTarget StationAirlock, AirlockDoorVerbTarget DestinationAirlock) CreateDockedConnection(SceneTree sceneTree)
    {
        var homeShip = AutoFree(new ShipSim { WestCorridorLength = 2, HasLifeSupport = true });
        sceneTree.Root.AddChild(homeShip);

        var station = AutoFree(new ShipSim { GridWidth = 6, EastCorridorLength = 2, HasLifeSupport = true });
        sceneTree.Root.AddChild(station);

        // Docked defaults true on both — this connection represents the Home Ship's currently-
        // docked Station, matching TravelConsoleVerbTarget's own ApplyCurrentLocation wiring.
        var destinationAirlock = AutoFree(new AirlockDoorVerbTarget
        {
            ShipARef = station,
            TileA = new Vector2I(7, 2),
            OwnsBridge = false,
            SealsLocalEdge = true,
            LocalEdgeColumnNear = 5,
            LocalEdgeColumnFar = 6,
        });
        sceneTree.Root.AddChild(destinationAirlock);

        var stationAirlock = AutoFree(new AirlockDoorVerbTarget
        {
            ShipARef = homeShip,
            ShipBRef = station,
            TileA = new Vector2I(-2, 2),
            TileB = new Vector2I(7, 2),
            SealsLocalEdge = true,
            LocalEdgeColumnNear = -1,
            LocalEdgeColumnFar = 0,
            PartnerDoorRef = destinationAirlock,
        });
        sceneTree.Root.AddChild(stationAirlock);

        // Bidirectional — RebindFarSide sets this both ways when the far side actually changes,
        // but the initial construction (matching the currently-docked Station already) has to
        // set it by hand, same as ShipBRef itself already needs to.
        destinationAirlock.PartnerDoorRef = stationAirlock;

        return (homeShip, station, stationAirlock, destinationAirlock);
    }

    private static void TickAll(int frames, params Node[] nodes)
    {
        for (var i = 0; i < frames; i++)
        {
            foreach (var node in nodes)
            {
                node._PhysicsProcess(1.0);
            }
        }
    }

    private static readonly CellCoord HomeShipMainRoomCell = new(0, 2);
    private static readonly CellCoord StationMainRoomCell = new(0, 2);
    private static readonly CellCoord StationHazardCell = new(2, 0); // main room, away from the corridor doorway

    [TestCase]
    [RequireGodotRuntime]
    public void BothDoorsOpen_PropagatesARealStationHazardToTheHomeShip()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (homeShip, station, stationAirlock, destinationAirlock) = CreateDockedConnection(sceneTree);

        station.Deck.BreachHull(StationHazardCell);
        stationAirlock.ApplySaveState(true);
        destinationAirlock.ApplySaveState(true);

        TickAll(50, stationAirlock, destinationAirlock, homeShip, station);

        // A genuinely, fully open connection to a real hazard is supposed to let it through —
        // this is normal, expected airlock behavior, not the bug.
        AssertFloat(homeShip.VolumeAt(HomeShipMainRoomCell).O2Fraction).IsLess(0.01);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void HomeSideDoorOpenAlone_StationSideClosed_ContainsTheStationHazard()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (homeShip, station, stationAirlock, destinationAirlock) = CreateDockedConnection(sceneTree);

        station.Deck.BreachHull(StationHazardCell);
        stationAirlock.ApplySaveState(true);
        // destinationAirlock stays closed.

        TickAll(50, stationAirlock, destinationAirlock, homeShip, station);

        // Sanity check the hazard is real...
        AssertFloat(station.VolumeAt(StationMainRoomCell).O2Fraction).IsLess(0.01);
        // ...but with the Station's own door closed, the bridge never engages — this is the
        // literal containment the original bug lacked (opening only the Home Ship's own side
        // used to flood the whole ship regardless of the far door).
        AssertFloat(homeShip.VolumeAt(HomeShipMainRoomCell).O2Fraction).IsEqual(AtmosphereVolume.Breathable.O2Fraction);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ClosingEitherDoor_ReSealsAndTheHomeShipRecovers()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (homeShip, station, stationAirlock, destinationAirlock) = CreateDockedConnection(sceneTree);

        station.Deck.BreachHull(StationHazardCell);
        stationAirlock.ApplySaveState(true);
        destinationAirlock.ApplySaveState(true);

        TickAll(50, stationAirlock, destinationAirlock, homeShip, station);
        var ventedO2 = homeShip.VolumeAt(HomeShipMainRoomCell).O2Fraction;
        AssertFloat(ventedO2).IsLess(0.01);

        // Closing the Station's own door (not the Home Ship's) — matches the reported symptom
        // ("close either airlock door, O2 goes back up"): the Home Ship's life support can now
        // regenerate its now-resealed main room.
        destinationAirlock.ApplySaveState(false);
        TickAll(50, stationAirlock, destinationAirlock, homeShip, station);

        AssertFloat(homeShip.VolumeAt(HomeShipMainRoomCell).O2Fraction).IsGreater(ventedO2);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void NonCurrentStationsDestinationDoorOpenedAlone_VentsOnlyThatStationsOwnHull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (homeShip, station, stationAirlock, destinationAirlock) = CreateDockedConnection(sceneTree);
        stationAirlock.ApplySaveState(true);
        destinationAirlock.ApplySaveState(true);

        // A second Station, not currently docked — StationAirlock's PartnerDoorRef still points
        // at the first Station's destinationAirlock, never at this one, so nothing here can ever
        // reach the Home Ship or Station A no matter what.
        var stationB = AutoFree(new ShipSim { GridWidth = 6, EastCorridorLength = 2, HasLifeSupport = true });
        sceneTree.Root.AddChild(stationB);

        var stationBDestinationAirlock = AutoFree(new AirlockDoorVerbTarget
        {
            ShipARef = stationB,
            TileA = new Vector2I(7, 2),
            OwnsBridge = false,
            SealsLocalEdge = true,
            LocalEdgeColumnNear = 5,
            LocalEdgeColumnFar = 6,
            Docked = false, // not the current destination
        });
        sceneTree.Root.AddChild(stationBDestinationAirlock);

        stationBDestinationAirlock.ApplySaveState(true); // the "wrong door," opened out of curiosity

        TickAll(50, stationAirlock, destinationAirlock, stationBDestinationAirlock, homeShip, station, stationB);

        AssertFloat(stationB.VolumeAt(StationMainRoomCell).O2Fraction).IsLess(0.01);
        AssertFloat(homeShip.VolumeAt(HomeShipMainRoomCell).O2Fraction).IsEqual(AtmosphereVolume.Breathable.O2Fraction);
        AssertFloat(station.VolumeAt(StationMainRoomCell).O2Fraction).IsEqual(AtmosphereVolume.Breathable.O2Fraction);
    }
}
