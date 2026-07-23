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
    public void BatteryFixtureActingAsSource_PowersConnectedMachine()
    {
        var deck = new Deck();
        deck.AddFixture(new BatteryFixture("battery-fixture", new CellCoord(0, 0), FixtureSurface.WallInner));
        deck.AddFixture(new MachineFixture("lamp", new CellCoord(1, 0), FixtureSurface.CeilingUnderside));

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery-fixture"));

        Assert.True(system.IsPowered(new PowerNodeId("lamp")));
    }

    [Fact]
    public void ThrusterFixtureActingAsConsumer_IsPoweredWhenConnectedToASource()
    {
        var deck = new Deck();
        deck.AddFixture(new BatteryFixture("battery-fixture", new CellCoord(0, 0), FixtureSurface.WallInner));
        deck.AddFixture(new ThrusterFixture("thruster", new CellCoord(1, 0), FixtureSurface.WallInner));

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery-fixture"));

        Assert.True(system.IsPowered(new PowerNodeId("thruster")));
    }

    [Fact]
    public void ThrusterFixture_IsNotPowered_WhenNotConnectedToAnySource()
    {
        var deck = new Deck();
        deck.AddFixture(new BatteryFixture("battery-fixture", new CellCoord(0, 0), FixtureSurface.WallInner));
        deck.AddFixture(new ThrusterFixture("thruster", new CellCoord(5, 5), FixtureSurface.WallInner));

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery-fixture"));

        Assert.False(system.IsPowered(new PowerNodeId("thruster")));
    }

    [Fact]
    public void ThrusterFixtureWithNoCharge_IsNeverPowered_EvenWhenDirectlyAdjacentToASource()
    {
        var deck = new Deck();
        deck.AddFixture(new BatteryFixture("battery-fixture", new CellCoord(0, 0), FixtureSurface.WallInner));
        deck.AddFixture(new ThrusterFixture("dead-thruster", new CellCoord(1, 0), FixtureSurface.WallInner) { Condition = 0f });

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery-fixture"));

        Assert.False(system.IsPowered(new PowerNodeId("dead-thruster")));
    }

    [Fact]
    public void ThrusterFixtureWithNoCharge_DoesNotRelayPowerToWhateversWiredBeyondIt()
    {
        var deck = new Deck();
        deck.AddFixture(new BatteryFixture("battery-fixture", new CellCoord(0, 0), FixtureSurface.WallInner));
        deck.AddFixture(new ThrusterFixture("dead-thruster", new CellCoord(1, 0), FixtureSurface.WallInner) { Condition = 0f });
        deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

        var system = new PowerSystem(deck);
        system.MarkSource(new PowerNodeId("battery-fixture"));

        // Not being used means it neither draws power itself (covered above) nor conducts it
        // through to whatever's wired only via it — the dead thruster breaks this chain.
        Assert.False(system.IsPowered(new PowerNodeId("lamp")));
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

    /// <summary>PoweredNodes caches its result and self-checks that cache against the deck's own
    /// fixture state (see PowerSystem.CacheIsValid), rather than relying on every mutator
    /// remembering to invalidate. These cover the four ways that state can change *without* anyone
    /// telling the PowerSystem — each one asks a question first, specifically so a stale cache
    /// exists to be caught.</summary>
    public class CacheInvalidation
    {
        [Fact]
        public void TogglingASwitchAfterAQuery_IsReflectedImmediately()
        {
            var deck = new Deck();
            deck.AddFixture(new BatteryFixture("battery", new CellCoord(0, 0), FixtureSurface.WallInner));
            deck.AddFixture(new SwitchFixture("switch", new CellCoord(1, 0), FixtureSurface.WallInner));
            deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

            var system = new PowerSystem(deck);
            system.MarkSource(new PowerNodeId("battery"));

            Assert.True(system.IsPowered(new PowerNodeId("lamp"))); // primes the cache
            ((SwitchFixture)deck.Fixtures.Single(f => f.Id == "switch")).IsOpen = true;

            Assert.False(system.IsPowered(new PowerNodeId("lamp")));
        }

        /// <summary>The case that made an explicit-invalidation design unsafe: TravelConsoleVerbTarget
        /// drains thruster charge by writing Condition straight onto Deck.Fixtures, with no reference
        /// to the PowerSystem at all — and a thruster's conductivity is charge-gated.</summary>
        [Fact]
        public void DrainingAThrusterToEmptyAfterAQuery_StopsItRelayingImmediately()
        {
            var deck = new Deck();
            deck.AddFixture(new BatteryFixture("battery", new CellCoord(0, 0), FixtureSurface.WallInner));
            deck.AddFixture(new ThrusterFixture("thruster", new CellCoord(1, 0), FixtureSurface.WallInner) { Condition = 1f });
            deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

            var system = new PowerSystem(deck);
            system.MarkSource(new PowerNodeId("battery"));

            Assert.True(system.IsPowered(new PowerNodeId("lamp"))); // primes the cache
            deck.Fixtures.Single(f => f.Id == "thruster").Condition = 0f;

            Assert.False(system.IsPowered(new PowerNodeId("thruster")));
            Assert.False(system.IsPowered(new PowerNodeId("lamp")));
        }

        [Fact]
        public void AddingAndRemovingFixturesAfterAQuery_IsReflectedImmediately()
        {
            var deck = new Deck();
            deck.AddFixture(new BatteryFixture("battery", new CellCoord(0, 0), FixtureSurface.WallInner));
            deck.AddFixture(new MachineFixture("lamp", new CellCoord(3, 0), FixtureSurface.CeilingUnderside));

            var system = new PowerSystem(deck);
            system.MarkSource(new PowerNodeId("battery"));

            Assert.False(system.IsPowered(new PowerNodeId("lamp"))); // primes the cache: gap at x=1,2

            deck.AddFixture(new ConduitFixture("conduit-1", new CellCoord(1, 0), FixtureSurface.FloorUnderside));
            deck.AddFixture(new ConduitFixture("conduit-2", new CellCoord(2, 0), FixtureSurface.FloorUnderside));
            Assert.True(system.IsPowered(new PowerNodeId("lamp")));

            deck.RemoveFixture("conduit-2");
            Assert.False(system.IsPowered(new PowerNodeId("lamp")));
        }

        /// <summary>Wear decays Condition on essentially every fixture every tick, and it must NOT
        /// invalidate — a cache that rebuilt every frame would defeat the whole point. Conduit and
        /// machine conductivity is condition-independent (unlike a thruster's), so a worn-down
        /// conduit still carries power, exactly as it did before caching existed.</summary>
        [Fact]
        public void PassiveWearOnAConduit_DoesNotChangeWhatIsPowered()
        {
            var deck = new Deck();
            deck.AddFixture(new BatteryFixture("battery", new CellCoord(0, 0), FixtureSurface.WallInner));
            deck.AddFixture(new ConduitFixture("conduit", new CellCoord(1, 0), FixtureSurface.FloorUnderside));
            deck.AddFixture(new MachineFixture("lamp", new CellCoord(2, 0), FixtureSurface.CeilingUnderside));

            var system = new PowerSystem(deck);
            system.MarkSource(new PowerNodeId("battery"));

            Assert.True(system.IsPowered(new PowerNodeId("lamp")));
            deck.Fixtures.Single(f => f.Id == "conduit").Condition = 0.01f;

            Assert.True(system.IsPowered(new PowerNodeId("lamp")));
        }

        [Fact]
        public void MarkingANewSourceAfterAQuery_IsReflectedImmediately()
        {
            var deck = new Deck();
            deck.AddFixture(new BatteryFixture("battery", new CellCoord(0, 0), FixtureSurface.WallInner));
            deck.AddFixture(new MachineFixture("lamp", new CellCoord(1, 0), FixtureSurface.CeilingUnderside));

            var system = new PowerSystem(deck);

            Assert.False(system.IsPowered(new PowerNodeId("lamp"))); // primes the cache: no sources yet
            system.MarkSource(new PowerNodeId("battery"));

            Assert.True(system.IsPowered(new PowerNodeId("lamp")));
        }
    }
}
