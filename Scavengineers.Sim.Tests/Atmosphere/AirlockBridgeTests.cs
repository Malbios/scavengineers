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
    public void Open_WithNeitherSideBreached_ConvergesTowardSharedAverage()
    {
        // Neither side has its own path to outside here, so the bridge just pools the two cells
        // toward each other — the direct-to-vacuum branch (see the tests below) only kicks in
        // once one side already has its own leak. Every other test in this file uses a breached
        // derelict cell, which always trips that branch — this is the one test that actually
        // exercises plain averaging.
        var (home, homeCell) = BreathableSystem();

        var staleCell = new CellCoord(0, 0);
        var staleDeck = new Deck();
        staleDeck.AddCell(staleCell);
        var stale = new AtmosphereSystem(staleDeck, AtmosphereVolume.Breathable with { O2Fraction = 0.05 });

        var bridge = new AirlockBridge(home, homeCell, stale, staleCell) { IsOpen = true };
        bridge.Tick(1);

        Assert.True(home.VolumeAt(homeCell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction);
        Assert.True(stale.VolumeAt(staleCell).O2Fraction > 0.05);
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
        // Threshold re-verified after the "pull both sides toward Vacuum directly" fix (see
        // AirlockBridge's own doc comment): since the derelict cell here is the breach cell
        // itself, IsConnectedToOutside is true from the first tick, so the doorway cell is pulled
        // straight toward Vacuum rather than toward a moving average with the derelict's own
        // (already near-vacuum) reading — converges to ~3.6% within 3 seconds, well under the
        // old ~0.10 (pre-fix, average-based) figure.
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
            home.VolumeAt(doorwayCell).O2Fraction < 0.05,
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

    [Fact]
    public void Open_WithABreachElsewhereInTheDerelictRoom_StillRapidlyDrainsTheHomeShipToo()
    {
        // The bug this branch exists for: a multi-cell derelict room whose hull breach is *not*
        // at the doorway cell used to let the doorway-side average settle into a stable,
        // deceptively-safe plateau — the breach only reached the doorway via Diffuse's much
        // gentler rate, never Vent's full strength, so a regenerating Home Ship could hold it
        // there indefinitely (confirmed via a throwaway probe reproducing exactly this setup,
        // before this fix). IsConnectedToOutside means the bridge now treats the open airlock as
        // part of that leak regardless of which cell in the derelict room the actual hole is in —
        // per the explicit design call, the *source* (Home, full of air, with life support) must
        // also rapidly drain, not sit safe. Home hits ~0.7% within 2s (probe) and stays there —
        // asserting at 5s with a loose 2% bound leaves plenty of margin either way.
        var doorwayCell = new CellCoord(0, 0);
        var breachCell = new CellCoord(4, 0); // several hops from the doorway, not the same cell

        var derelictDeck = new Deck();
        for (var i = 0; i < 5; i++)
        {
            derelictDeck.AddCell(new CellCoord(i, 0));
        }
        derelictDeck.BreachHull(breachCell);
        var derelict = new AtmosphereSystem(derelictDeck);

        var homeDeck = new Deck();
        homeDeck.AddCell(doorwayCell);
        var home = new AtmosphereSystem(homeDeck, hasLifeSupport: true);

        var bridge = new AirlockBridge(home, doorwayCell, derelict, doorwayCell) { IsOpen = true };

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 300; i++) // 5s of real gameplay frames
        {
            home.Tick(frameDt);
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            home.VolumeAt(doorwayCell).O2Fraction < 0.02,
            $"Home should have rapidly drained once bridged to a room with its own (indirect) breach, was {home.VolumeAt(doorwayCell).O2Fraction}");
    }
}
