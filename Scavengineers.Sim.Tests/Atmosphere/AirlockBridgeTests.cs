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
}
