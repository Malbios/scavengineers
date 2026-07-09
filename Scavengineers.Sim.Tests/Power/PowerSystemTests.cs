using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Power;

public class PowerSystemTests
{
    [Fact]
    public void MachineConnectedThroughClosedSwitch_IsPowered()
    {
        var deck = new Deck();
        deck.AddFixture(new MachineFixture("battery", new CellCoord(0, 0), FixtureSurface.FloorTop));
        deck.AddFixture(new SwitchFixture("switch", new CellCoord(1, 0), FixtureSurface.WallInner));
        deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery"));

        Assert.True(system.IsPowered(new PowerNodeId("lamp")));
    }

    [Fact]
    public void OpeningASwitch_CutsPowerToJustThatRegion()
    {
        var deck = new Deck();
        deck.AddFixture(new MachineFixture("battery", new CellCoord(0, 0), FixtureSurface.FloorTop));
        deck.AddFixture(new SwitchFixture("switch", new CellCoord(1, 0), FixtureSurface.WallInner));
        deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));
        deck.AddFixture(new MachineFixture("unrelated-machine", new CellCoord(0, 1), FixtureSurface.FloorTop));

        var lightSwitch = (SwitchFixture)deck.Fixtures.Single(f => f.Id == "switch");
        lightSwitch.IsOpen = true;

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery"));

        Assert.False(system.IsPowered(new PowerNodeId("lamp")));
        Assert.True(system.IsPowered(new PowerNodeId("unrelated-machine")));
        Assert.True(system.IsPowered(new PowerNodeId("battery")));
    }

    [Fact]
    public void ClosingASwitchAgain_RestoresPower()
    {
        var deck = new Deck();
        deck.AddFixture(new MachineFixture("battery", new CellCoord(0, 0), FixtureSurface.FloorTop));
        deck.AddFixture(new SwitchFixture("switch", new CellCoord(1, 0), FixtureSurface.WallInner));
        deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

        var lightSwitch = (SwitchFixture)deck.Fixtures.Single(f => f.Id == "switch");
        lightSwitch.IsOpen = true;

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery"));

        lightSwitch.IsOpen = false;

        Assert.True(system.IsPowered(new PowerNodeId("lamp")));
    }

    [Fact]
    public void NodeNotConnectedToAnySource_IsNotPowered()
    {
        var deck = new Deck();
        deck.AddFixture(new MachineFixture("lamp-a", new CellCoord(0, 0), FixtureSurface.FloorTop));
        deck.AddFixture(new MachineFixture("lamp-b", new CellCoord(1, 0), FixtureSurface.FloorTop));

        var system = new PowerSystem(deck);

        Assert.False(system.IsPowered(new PowerNodeId("lamp-a")));
    }
}
