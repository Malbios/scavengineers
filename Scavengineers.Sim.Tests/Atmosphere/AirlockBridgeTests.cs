using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Atmosphere;

public class AirlockBridgeTests
{
    private static (AtmosphereSystem System, CellCoord Cell) BreathableSystem()
    {
        var cell = new CellCoord(0, 0);
        var deck = new Deck();
        deck.AddCell(cell);
        return (new AtmosphereSystem(deck), cell);
    }

    private static (AtmosphereSystem System, CellCoord Cell) BreachedSystem()
    {
        var cell = new CellCoord(0, 0);
        var deck = new Deck();
        deck.AddCell(cell);
        deck.BreachHull(cell);
        return (new AtmosphereSystem(deck), cell);
    }

    [Fact]
    public void Closed_LeavesBothSystemsUnaffected()
    {
        var (home, homeCell) = BreathableSystem();
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // drive the derelict down toward vacuum while isolated

        var derelictPressureBefore = derelict.VolumeAt(derelictCell).Pressure;

        var bridge = new AirlockBridge(home, homeCell, derelict, derelictCell);
        bridge.Tick(10);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, home.VolumeAt(homeCell).Pressure);
        Assert.Equal(derelictPressureBefore, derelict.VolumeAt(derelictCell).Pressure);
    }

    [Fact]
    public void Open_ConvergesBothSidesTowardEachOther()
    {
        var (home, homeCell) = BreathableSystem();
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // derelict is now near-vacuum, isolated

        var bridge = new AirlockBridge(home, homeCell, derelict, derelictCell) { IsOpen = true };
        bridge.Tick(1);

        Assert.True(home.VolumeAt(homeCell).Pressure < AtmosphereVolume.Breathable.Pressure);
        Assert.True(derelict.VolumeAt(derelictCell).Pressure > 0);
    }

    [Fact]
    public void Open_HomeShipEventuallyApproachesDerelictWhileBreached()
    {
        var (home, homeCell) = BreathableSystem();
        var (derelict, derelictCell) = BreachedSystem();

        var bridge = new AirlockBridge(home, homeCell, derelict, derelictCell) { IsOpen = true };

        for (var i = 0; i < 100; i++)
        {
            derelict.Tick(1); // derelict keeps venting to outside independently
            bridge.Tick(1);
        }

        Assert.True(home.VolumeAt(homeCell).Pressure < 10);
    }

    [Fact]
    public void Open_OnlyDirectlyExchangesTheDoorwayCell_FarCellsLagBehindAndCatchUpLater()
    {
        // The bridge now only exchanges the two named cells directly (see AirlockBridge's own
        // doc comment) — a far cell only feels the effect via the home ship's own internal
        // per-cell diffusion carrying it inward, hop by hop. This is the intended replacement for
        // the old "whole connected room moves in lockstep" behavior, not a regression of it.
        var doorwayCell = new CellCoord(0, 0);
        var homeDeck = new Deck();
        for (var i = 0; i < 10; i++)
        {
            homeDeck.AddCell(new CellCoord(i, 0));
        }

        var home = new AtmosphereSystem(homeDeck);
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // derelict is now near-vacuum, isolated

        var bridge = new AirlockBridge(home, doorwayCell, derelict, derelictCell) { IsOpen = true };
        var farCell = new CellCoord(9, 0); // 9 hops from the doorway

        for (var i = 0; i < 20; i++)
        {
            home.Tick(1); // the ship's own internal diffusion, every tick, same as ShipSim
            derelict.Tick(1); // derelict keeps venting to outside independently, same as ShipSim
            bridge.Tick(1);
        }

        // Shortly after opening, the doorway cell has dropped notably but the far cell has
        // barely moved — this is the "keeps some air for a moment" behavior replacing the old
        // "whole room reacts almost as fast as the doorway" bug.
        Assert.True(home.VolumeAt(doorwayCell).Pressure < AtmosphereVolume.Breathable.Pressure * 0.5);
        Assert.True(home.VolumeAt(farCell).Pressure > AtmosphereVolume.Breathable.Pressure * 0.9);

        // Given enough total real time, diffusion eventually carries the effect the whole way
        // down the corridor — the far cell isn't permanently immune, just delayed.
        for (var i = 0; i < 500; i++)
        {
            derelict.Tick(1);
            home.Tick(1);
            bridge.Tick(1);
        }

        Assert.True(home.VolumeAt(farCell).Pressure < AtmosphereVolume.Breathable.Pressure * 0.5);
    }

    [Fact]
    public void Open_BreachedRoomRapidlySettlesNearVacuum_EvenAtRealisticFrameRates()
    {
        // A brief mixing blip right as the airlock opens is expected (both sides are being pulled
        // toward the same shared average), but it must settle back to near vacuum within a
        // couple of seconds, not linger at an elevated shared value for as long as the airlock
        // stays open — checked at a realistic per-physics-frame dt (60fps), not the large
        // dt-per-Tick used elsewhere.
        var (home, homeCell) = BreathableSystem();
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // derelict starts fully vented, isolated

        var bridge = new AirlockBridge(home, homeCell, derelict, derelictCell) { IsOpen = true };

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 180; i++) // 3 seconds of real gameplay frames, airlock held open
        {
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            derelict.VolumeAt(derelictCell).O2Fraction < 0.01,
            $"derelict O2 should have settled near vacuum within 3s, was {derelict.VolumeAt(derelictCell).O2Fraction}");
    }

    [Fact]
    public void Open_RapidlyVentsTheHomeRoomToo_MatchingTheBreachsOwnSpeed()
    {
        // Design choice, not a bug: once the airlock is open, any path to outer space should feel
        // dangerous immediately, not just for the room with the actual hole — a slower bridge
        // rate was tried first (treating the airlock as a narrow chokepoint so a quick transit
        // wouldn't cost air), but that undersold the danger of opening an airlock into a breached
        // room at all. The bridge's rate matches AtmosphereSystem's own vent rate, so the home
        // room's connected air drains just as fast as the breach itself.
        //
        // Threshold re-verified after the diffusion redesign: the bridge now only exchanges the
        // doorway cell directly (not the whole averaged room), and that cell's own neighbor keeps
        // diffusing a little air back into it each tick, so it settles just above the old 0.1
        // cutoff (~0.10 vs comfortably under before) rather than exactly matching the pre-redesign
        // number — still more than halved from Breathable within 3 seconds, still clearly rapid.
        var doorwayCell = new CellCoord(0, 0);
        var homeDeck = new Deck();
        for (var i = 0; i < 5; i++)
        {
            homeDeck.AddCell(new CellCoord(i, 0));
        }

        var home = new AtmosphereSystem(homeDeck);
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // derelict is now near-vacuum, isolated

        var bridge = new AirlockBridge(home, doorwayCell, derelict, derelictCell) { IsOpen = true };

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 180; i++) // 3 seconds — even a quick transit
        {
            home.Tick(frameDt);
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            home.VolumeAt(doorwayCell).O2Fraction < 0.12,
            $"a few seconds with the airlock open should rapidly drain the home room too, was {home.VolumeAt(doorwayCell).O2Fraction}");
    }

    [Fact]
    public void Open_WithASealedOffRoom_LeavesThatRoomUnaffected()
    {
        // If the home ship's own interior door has sealed off a room from the doorway tile,
        // the bridge should only touch the connected room, not the whole ship.
        var doorwayCell = new CellCoord(0, 0);
        var sealedCell = new CellCoord(1, 0);
        var homeDeck = new Deck();
        homeDeck.AddCell(doorwayCell);
        homeDeck.AddCell(sealedCell);
        homeDeck.SealEdge(doorwayCell, sealedCell);

        var home = new AtmosphereSystem(homeDeck);
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50);

        var bridge = new AirlockBridge(home, doorwayCell, derelict, derelictCell) { IsOpen = true };
        bridge.Tick(10);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, home.VolumeAt(sealedCell).Pressure);
        Assert.True(home.VolumeAt(doorwayCell).Pressure < AtmosphereVolume.Breathable.Pressure);
    }
}
