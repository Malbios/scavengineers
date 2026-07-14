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
            // home's own Tick is what actually consumes the bridge's mark and vents its
            // component now — the bridge itself no longer mutates cell volumes directly.
            home.Tick(1);
            derelict.Tick(1); // derelict keeps venting to outside independently
            bridge.Tick(1);
        }

        Assert.True(home.VolumeAt(homeCell).Pressure < 10);
    }

    [Fact]
    public void Open_VentsTheWholeHomeShipTogether_NoDistanceBasedLag()
    {
        // Once the doorway cell is marked externally vented, home's OWN Tick vents its whole
        // connected component uniformly (see AtmosphereSystem's own Vent doc comment) - matching
        // realistic depressurization, where internal pressure equalizes far faster than air
        // escapes through a hole, so a far cell doesn't get a "moment of grace."
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
        var farCell = new CellCoord(9, 0); // 9 hops from the doorway - shouldn't matter anymore

        for (var i = 0; i < 60; i++) // 1 second
        {
            home.Tick(1.0 / 60);
            derelict.Tick(1.0 / 60);
            bridge.Tick(1.0 / 60);
        }

        // Doorway and far cell have dropped together, not one lagging the other.
        Assert.Equal(home.VolumeAt(doorwayCell).Pressure, home.VolumeAt(farCell).Pressure, precision: 3);
        Assert.True(home.VolumeAt(farCell).Pressure < AtmosphereVolume.Breathable.Pressure * 0.1);
    }

    [Fact]
    public void Open_BreachedRoomRapidlySettlesNearVacuum_EvenAtRealisticFrameRates()
    {
        // Derelict already has its own real breach, so it vents via its own component regardless
        // of the bridge — this just confirms that still holds at a realistic per-physics-frame dt
        // (60fps), not the large dt-per-Tick used elsewhere.
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
        // Threshold re-verified after the whole-component-vent redesign: once marked, home's own
        // Tick vents its entire 5-cell component uniformly at VentRatePerSecond, not just the
        // doorway cell — converges to essentially zero (~3.6e-6 %) within 3 seconds, since a
        // whole small component now empties just as fast as a single cell would.
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
            home.VolumeAt(doorwayCell).O2Fraction < 0.001,
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
        bridge.Tick(10); // marks doorwayCell as externally vented
        home.Tick(10); // home's own Tick vents the marked cell's component (sealedCell is separate)

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, home.VolumeAt(sealedCell).Pressure);
        Assert.True(home.VolumeAt(doorwayCell).Pressure < AtmosphereVolume.Breathable.Pressure);
    }

    [Fact]
    public void Open_WithABreachElsewhereInTheDerelictRoom_StillRapidlyDrainsTheHomeShipToo()
    {
        // IsConnectedToOutside checks the derelict's whole connected component, not just the
        // bridged cell — so a breach anywhere in that component (not necessarily at the doorway)
        // still marks Home's side as leaking, and Home's own Tick then vents its whole component
        // uniformly. Per the explicit design call, the source (Home, full of air, with life
        // support) must also rapidly drain, not sit safe just because the actual hole is
        // elsewhere in the derelict.
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

    [Fact]
    public void Open_WithLifeSupport_LifeSupportDoesNotBlockTheWholeComponentVent()
    {
        // Vent and Regenerate are mutually exclusive per component now (see Tick) - once marked,
        // a component is vented, full stop, regardless of life support or room size. This
        // confirms life support doesn't slow or block that at all: every cell in a multi-cell,
        // life-support-equipped Home still converges to near-vacuum together with the doorway,
        // not held at a safe plateau by regen (the old failure mode this branch used to guard
        // against via Diffuse-dilution, which no longer applies since Diffuse never runs for a
        // vented component).
        var doorwayCell = new CellCoord(0, 0);
        var farCell = new CellCoord(7, 0);
        var homeDeck = new Deck();
        for (var i = 0; i < 8; i++) // representative multi-cell ship, not just one cell
        {
            homeDeck.AddCell(new CellCoord(i, 0));
        }
        var home = new AtmosphereSystem(homeDeck, hasLifeSupport: true);

        var breachCell = new CellCoord(4, 0); // indirect breach, not at the doorway
        var derelictDeck = new Deck();
        for (var i = 0; i < 5; i++)
        {
            derelictDeck.AddCell(new CellCoord(i, 0));
        }
        derelictDeck.BreachHull(breachCell);
        var derelict = new AtmosphereSystem(derelictDeck);

        var bridge = new AirlockBridge(home, doorwayCell, derelict, new CellCoord(0, 0)) { IsOpen = true };

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 180; i++) // 3s
        {
            home.Tick(frameDt);
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            home.VolumeAt(doorwayCell).O2Fraction < 0.001,
            $"doorway should be near-vacuum by 3s despite life support, was {home.VolumeAt(doorwayCell).O2Fraction}");
        Assert.Equal(home.VolumeAt(doorwayCell).O2Fraction, home.VolumeAt(farCell).O2Fraction, precision: 3);
    }
}
