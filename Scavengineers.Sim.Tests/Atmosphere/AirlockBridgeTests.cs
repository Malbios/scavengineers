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
    public void Open_AffectsTheWholeConnectedRoomNotJustTheNamedTile()
    {
        // Regression: a multi-cell ship's own Equalize used to dilute the bridge's effect on a
        // single named tile almost to nothing. The bridge must move the *whole* connected
        // volume together with the ship's own Tick, not fight it.
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

        for (var i = 0; i < 20; i++)
        {
            home.Tick(1); // the ship's own internal equalize, every tick, same as ShipSim
            bridge.Tick(1);
        }

        // Every cell in the home ship's connected volume should have dropped noticeably —
        // not just the doorway tile, and not by the old bug's diluted fraction of a percent
        // (10 home cells outnumber 1 derelict cell, so full equilibrium settles well above
        // the derelict's own near-vacuum level — the point is "noticeable", not "equal").
        var farCell = new CellCoord(9, 0);
        Assert.True(home.VolumeAt(farCell).Pressure < AtmosphereVolume.Breathable.Pressure * 0.95);
    }

    [Fact]
    public void Open_BreachedRoomNeverMeaningfullyFillsWithAir_EvenAtRealisticFrameRates()
    {
        // Regression: at equal vent/bridge rates, opening the airlock into a breached room let
        // the bridge's push toward a shared average and the derelict's own vent settle into a
        // tug-of-war equilibrium holding several percent O2 for as long as the airlock stayed
        // open — the room visibly "got a bit of air" rather than just a brief moment. Vent must
        // decisively outpace the bridge's equalize rate so this never happens, checked here at a
        // realistic per-physics-frame dt (60fps), not the large dt-per-Tick used elsewhere.
        var (home, homeCell) = BreathableSystem();
        var (derelict, derelictCell) = BreachedSystem();
        derelict.Tick(50); // derelict starts fully vented, isolated

        var bridge = new AirlockBridge(home, homeCell, derelict, derelictCell) { IsOpen = true };

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 300; i++) // 5 seconds of real gameplay frames, airlock held open
        {
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);

            Assert.True(
                derelict.VolumeAt(derelictCell).O2Fraction < 0.02,
                $"O2 rose to {derelict.VolumeAt(derelictCell).O2Fraction} at frame {i}");
        }
    }

    [Fact]
    public void Open_QuickTransitBarelyDrainsTheHomeRoom_ButLeavingItOpenGraduallyDoes()
    {
        // Regression: making AtmosphereSystem's own vent rate decisively outpace the bridge (see
        // the sibling test above) had a side effect — the bridge's shared average is now pulled
        // much closer to true vacuum, and at the old bridge rate (equal to the vent rate before
        // that fix), a normal few-second airlock transit already cost the home room a third of
        // its air, with half a minute open fully draining it. The bridge's own rate must be much
        // slower than any within-a-deck rate, so a quick transit barely registers while a
        // long-left-open airlock still gradually drains a connected room (real tension, just slow).
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
        for (var i = 0; i < 300; i++) // 5 seconds — a normal quick transit
        {
            home.Tick(frameDt);
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            home.VolumeAt(doorwayCell).O2Fraction > AtmosphereVolume.Breathable.O2Fraction * 0.9,
            $"a 5s transit should barely dent the home room's air, was {home.VolumeAt(doorwayCell).O2Fraction}");

        for (var i = 0; i < 1500; i++) // 25 more seconds — airlock left open, 30s total
        {
            home.Tick(frameDt);
            derelict.Tick(frameDt);
            bridge.Tick(frameDt);
        }

        Assert.True(
            home.VolumeAt(doorwayCell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction * 0.8,
            $"leaving it open half a minute should still meaningfully drain the room, was {home.VolumeAt(doorwayCell).O2Fraction}");
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
