using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Tests.Atmosphere;

public class AtmosphereSystemTests
{
    [Fact]
    public void SealedRoom_HoldsItsScalars()
    {
        var cell = new CellCoord(0, 0);
        var system = new AtmosphereSystem([cell]);

        system.Tick(10);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(cell).Pressure);
        Assert.Equal(AtmosphereVolume.Breathable.O2Fraction, system.VolumeAt(cell).O2Fraction);
    }

    [Fact]
    public void BreachedHull_DrainsPressureAndO2OverTime()
    {
        var cell = new CellCoord(0, 0);
        var system = new AtmosphereSystem([cell]);
        system.BreachHull(cell);

        system.Tick(1);

        Assert.True(system.VolumeAt(cell).Pressure < AtmosphereVolume.Breathable.Pressure);
        Assert.True(system.VolumeAt(cell).O2Fraction < AtmosphereVolume.Breathable.O2Fraction);
    }

    [Fact]
    public void BreachedHull_ApproachesVacuumAsTimeAccumulates()
    {
        var cell = new CellCoord(0, 0);
        var system = new AtmosphereSystem([cell]);
        system.BreachHull(cell);

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
        var system = new AtmosphereSystem([roomA, roomB]);

        // Give room B a lower starting pressure than room A by briefly venting it while
        // sealed off, then repairing the breach and reconnecting the two rooms.
        system.SealEdge(roomA, roomB);
        system.BreachHull(roomB);
        system.Tick(1); // room B now has lower pressure than room A
        system.RepairHull(roomB);

        Assert.True(system.VolumeAt(roomB).Pressure < system.VolumeAt(roomA).Pressure);

        system.UnsealEdge(roomA, roomB);
        system.Tick(0); // dt = 0: equalize only, no additional venting

        Assert.Equal(system.VolumeAt(roomA).Pressure, system.VolumeAt(roomB).Pressure, precision: 6);
    }

    [Fact]
    public void UnrelatedSealedRoom_IsUnaffectedByABreachElsewhere()
    {
        var breached = new CellCoord(0, 0);
        var untouched = new CellCoord(5, 5);
        var system = new AtmosphereSystem([breached, untouched]);
        system.BreachHull(breached);

        system.Tick(5);

        Assert.Equal(AtmosphereVolume.Breathable.Pressure, system.VolumeAt(untouched).Pressure);
    }
}
