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
    public void TwoSealedRoomsJoinedByOpenDoor_EqualizeTowardSharedValue()
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

        Assert.True(system.VolumeAt(roomB).Pressure < system.VolumeAt(roomA).Pressure);

        deck.UnsealEdge(roomA, roomB);
        system.Tick(0); // dt = 0: equalize only, no additional venting

        Assert.Equal(system.VolumeAt(roomA).Pressure, system.VolumeAt(roomB).Pressure, precision: 6);
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

        system.Tick(1);

        Assert.Equal(system.VolumeAt(origin).Pressure, system.VolumeAt(extended).Pressure, precision: 6);
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
}
