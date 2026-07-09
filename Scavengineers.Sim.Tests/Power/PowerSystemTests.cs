using Scavengineers.Sim.Power;

namespace Scavengineers.Sim.Tests.Power;

public class PowerSystemTests
{
    [Fact]
    public void MachineConnectedThroughClosedSwitch_IsPowered()
    {
        var battery = new PowerNodeId("battery");
        var lightSwitch = new PowerNodeId("switch");
        var lamp = new PowerNodeId("lamp");

        var system = new PowerSystem();
        system.MarkSource(battery);
        system.Connect(battery, lightSwitch);
        system.Connect(lightSwitch, lamp);

        Assert.True(system.IsPowered(lamp));
    }

    [Fact]
    public void OpeningASwitch_CutsPowerToJustThatRegion()
    {
        var battery = new PowerNodeId("battery");
        var lightSwitch = new PowerNodeId("switch");
        var lamp = new PowerNodeId("lamp");
        var unrelatedMachine = new PowerNodeId("unrelated-machine");

        var system = new PowerSystem();
        system.MarkSource(battery);
        system.Connect(battery, lightSwitch);
        system.Connect(lightSwitch, lamp);
        system.Connect(battery, unrelatedMachine);

        system.OpenSwitch(lightSwitch, lamp);

        Assert.False(system.IsPowered(lamp));
        Assert.True(system.IsPowered(unrelatedMachine));
        Assert.True(system.IsPowered(battery));
    }

    [Fact]
    public void ClosingASwitchAgain_RestoresPower()
    {
        var battery = new PowerNodeId("battery");
        var lightSwitch = new PowerNodeId("switch");
        var lamp = new PowerNodeId("lamp");

        var system = new PowerSystem();
        system.MarkSource(battery);
        system.Connect(battery, lightSwitch);
        system.Connect(lightSwitch, lamp);
        system.OpenSwitch(lightSwitch, lamp);

        system.CloseSwitch(lightSwitch, lamp);

        Assert.True(system.IsPowered(lamp));
    }

    [Fact]
    public void NodeNotConnectedToAnySource_IsNotPowered()
    {
        var lampA = new PowerNodeId("lamp-a");
        var lampB = new PowerNodeId("lamp-b");

        var system = new PowerSystem();
        system.Connect(lampA, lampB);

        Assert.False(system.IsPowered(lampA));
    }
}
