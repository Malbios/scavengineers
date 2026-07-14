using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Atmosphere;

public class AtmosphereSystemTests
{
    private static Deck DeckWithCells(params CellCoord[] cells)
    {
        var deck = new Deck();
        foreach (var cell in cells)
        {
            deck.AddCell(cell);
        }
        return deck;
    }

    [Fact]
    public void SealedRoom_HoldsItsScalars()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        var system = new AtmosphereSystem(deck);

        system.Tick(10);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(cell).Pressure);
        Assert.Equal(AtmosphereVolume.Breathable.O2Fraction, system.VolumeAt(cell).O2Fraction);
    }

    [Fact]
    public void BreachedHull_DrainsPressureAndO2OverTime()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        deck.BreachHull(cell);
        var system = new AtmosphereSystem(deck);

        system.Tick(1);

        Assert.True(system.VolumeAt(cell).Pressure < AtmosphereVolume.Breathable.Pressure);
        Assert.True(system.VolumeAt(cell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction);
    }

    [Fact]
    public void BreachedHull_DrainsTemperatureTowardVacuumOverTime()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        deck.BreachHull(cell);
        var system = new AtmosphereSystem(deck);

        system.Tick(1);

        Assert.True(system.VolumeAt(cell).Temperature < AtmosphereVolume.Breathable.Temperature);
    }

    [Fact]
    public void BreachedHull_ApproachesVacuumAsTimeAccumulates()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        deck.BreachHull(cell);
        var system = new AtmosphereSystem(deck);

        for (var i = 0; i < 50; i++)
        {
            system.Tick(1);
        }

        Assert.True(system.VolumeAt(cell).Pressure < 0.1);
    }

    [Fact]
    public void TwoRoomsJoinedByOpenDoor_GraduallyConvergeOverManyTicks()
    {
        var roomA = new CellCoord(0, 0);
        var roomB = new CellCoord(1, 0);
        var deck = DeckWithCells(roomA, roomB);

        // Give room B a lower starting pressure than room A by briefly venting it while
        // sealed off, then repairing the breach and reconnecting the two rooms.
        deck.SealEdge(roomA, roomB);
        deck.BreachHull(roomB);
        var system = new AtmosphereSystem(deck);
        system.Tick(1); // room B now has lower pressure than room A
        deck.RepairHull(roomB);

        var pressureBBeforeOpen = system.VolumeAt(roomB).Pressure;
        Assert.True(pressureBBeforeOpen < system.VolumeAt(roomA).Pressure);

        deck.UnsealEdge(roomA, roomB);

        // A single realistic-frame-dt tick nudges the two cells toward each other, not equal —
        // instant whole-component equalization is exactly what per-cell diffusion replaces.
        system.Tick(1.0 / 60.0);
        Assert.True(system.VolumeAt(roomB).Pressure > pressureBBeforeOpen);
        Assert.NotEqual(system.VolumeAt(roomA).Pressure, system.VolumeAt(roomB).Pressure);

        // Given enough real time, two directly-adjacent, unsealed cells fully converge.
        for (var i = 0; i < 300; i++)
        {
            system.Tick(1.0 / 60.0);
        }

        Assert.Equal(system.VolumeAt(roomA).Pressure, system.VolumeAt(roomB).Pressure, precision: 1);
    }

    [Fact]
    public void UnrelatedSealedRoom_IsUnaffectedByABreachElsewhere()
    {
        var breached = new CellCoord(0, 0);
        var untouched = new CellCoord(5, 5);
        var deck = DeckWithCells(breached, untouched);
        deck.BreachHull(breached);
        var system = new AtmosphereSystem(deck);

        system.Tick(5);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(untouched).Pressure);
    }

    [Fact]
    public void DepletedSealedRoom_WithoutLifeSupport_StaysDepleted()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        var system = new AtmosphereSystem(deck, AtmosphereVolume.Vacuum);

        system.Tick(50);

        Assert.Equal(AtmosphereVolume.Vacuum.O2Fraction, system.VolumeAt(cell).O2Fraction);
    }

    [Fact]
    public void DepletedSealedRoom_WithLifeSupport_RecoversTowardBreathableOverTime()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        var system = new AtmosphereSystem(deck, AtmosphereVolume.Vacuum, hasLifeSupport: true);

        for (var i = 0; i < 50; i++)
        {
            system.Tick(1);
        }

        Assert.True(system.VolumeAt(cell).O2Fraction > AtmosphereVolume.Vacuum.O2Fraction);
    }

    [Fact]
    public void BreachedRoom_WithLifeSupport_StillVentsInsteadOfRegenerating()
    {
        var cell = new CellCoord(0, 0);
        var deck = DeckWithCells(cell);
        deck.BreachHull(cell);
        var system = new AtmosphereSystem(deck, hasLifeSupport: true);

        system.Tick(1);

        Assert.True(system.VolumeAt(cell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction);
    }

    [Fact]
    public void AddCell_LetsTickReadANewlyExtendedCellWithoutThrowing()
    {
        var origin = new CellCoord(0, 0);
        var deck = DeckWithCells(origin);
        var system = new AtmosphereSystem(deck);

        var extended = new CellCoord(1, 0);
        deck.AddCell(extended);
        deck.BreachWallEdge(extended, new CellCoord(2, 0));
        system.AddCell(extended);

        system.Tick(1); // must not throw KeyNotFoundException for the freshly-extended cell

        // extended is directly breached and adjacent to origin (unsealed), so they're one vented
        // component now — both drop together, not one lagging the other (see Vent's whole-
        // component semantics).
        Assert.True(system.VolumeAt(extended).Pressure < AtmosphereVolume.Breathable.Pressure);
        Assert.Equal(system.VolumeAt(extended).Pressure, system.VolumeAt(origin).Pressure, precision: 6);
    }

    [Fact]
    public void RemoveCell_DropsItsVolume_SoANewCellAtTheSameCoordStartsFresh()
    {
        var cell = new CellCoord(1, 0);
        var deck = DeckWithCells(new CellCoord(0, 0));
        var system = new AtmosphereSystem(deck);
        deck.AddCell(cell);
        deck.BreachHull(cell);
        system.AddCell(cell);
        system.Tick(50); // drive it well below Breathable

        system.RemoveCell(cell);
        system.AddCell(cell);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(cell).Pressure);
    }

    [Fact]
    public void TwoTileWideDoor_SealedOnBothEdges_KeepsRoomsIndependent()
    {
        // A 2-tile-wide doorway (InteriorDoorVerbTarget's shape) is really two edges that must
        // both be sealed for the rooms to be properly independent — sealing only one would
        // leave a gap the flood-fill could route through.
        var roomA1 = new CellCoord(5, 2);
        var roomB1 = new CellCoord(6, 2);
        var roomA2 = new CellCoord(5, 3);
        var roomB2 = new CellCoord(6, 3);
        var deck = DeckWithCells(roomA1, roomB1, roomA2, roomB2);
        deck.SealEdge(roomA1, roomB1);
        deck.SealEdge(roomA2, roomB2);
        deck.BreachHull(roomB1);

        var system = new AtmosphereSystem(deck);
        system.Tick(50);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(roomA1).Pressure);
        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(roomA2).Pressure);
        Assert.True(system.VolumeAt(roomB1).Pressure < 0.1);
    }

    [Fact]
    public void BreachedCorridor_AllCellsVentTogether_NoDistanceBasedLag()
    {
        // Realistic depressurization: internal pressure equalizes far faster than air escapes
        // through the hole, so a connected volume has no time to develop a distance-based
        // gradient — every cell in a breached corridor vents in lockstep, not hop-by-hop delayed
        // (see Vent's own doc comment). Near and far cells start identical and get the identical
        // Lerp toward the identical target every tick, so they stay exactly equal, not just close.
        var deck = new Deck();
        const int corridorLength = 16;
        for (var i = 0; i < corridorLength; i++)
        {
            deck.AddCell(new CellCoord(i, 0));
        }

        var breachCell = new CellCoord(0, 0);
        var nearCell = new CellCoord(1, 0); // 1 hop from the breach
        var farCell = new CellCoord(10, 0); // 10 hops from the breach — shouldn't matter anymore
        deck.BreachHull(breachCell);
        var system = new AtmosphereSystem(deck);

        const double frameDt = 1.0 / 60.0;
        for (var i = 0; i < 60; i++) // 1 second
        {
            system.Tick(frameDt);
        }

        Assert.Equal(system.VolumeAt(breachCell).O2Fraction, system.VolumeAt(nearCell).O2Fraction, precision: 6);
        Assert.Equal(system.VolumeAt(nearCell).O2Fraction, system.VolumeAt(farCell).O2Fraction, precision: 6);
        Assert.True(system.VolumeAt(farCell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction * 0.1);
    }
}
