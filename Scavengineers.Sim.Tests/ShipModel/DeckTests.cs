using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.ShipModel;

public class DeckTests
{
    [Fact]
    public void SealEdge_IsSymmetric_RegardlessOfArgumentOrder()
    {
        var deck = new Deck();
        var a = new CellCoord(0, 0);
        var b = new CellCoord(1, 0);

        deck.SealEdge(a, b);

        Assert.True(deck.IsEdgeSealed(a, b));
        Assert.True(deck.IsEdgeSealed(b, a));
    }

    [Fact]
    public void UnsealEdge_RemovesTheSeal()
    {
        var deck = new Deck();
        var a = new CellCoord(0, 0);
        var b = new CellCoord(1, 0);
        deck.SealEdge(a, b);

        deck.UnsealEdge(a, b);

        Assert.False(deck.IsEdgeSealed(a, b));
    }

    [Fact]
    public void BreachAndRepairHull_ToggleIsHullBreached()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);

        Assert.False(deck.IsHullBreached(cell));

        deck.BreachHull(cell);
        Assert.True(deck.IsHullBreached(cell));
        Assert.Contains(cell, deck.HullBreaches);

        deck.RepairHull(cell);
        Assert.False(deck.IsHullBreached(cell));
    }

    [Fact]
    public void AddFixture_IsRetrievableFromFixturesList()
    {
        var deck = new Deck();
        var fixture = new ConduitFixture("conduit-1", new CellCoord(2, 3), FixtureSurface.WallOuter);

        deck.AddFixture(fixture);

        Assert.Contains(fixture, deck.Fixtures);
    }

    [Fact]
    public void RemoveFixture_DropsItFromTheFixturesList()
    {
        var deck = new Deck();
        var fixture = new ConduitFixture("conduit-1", new CellCoord(2, 3), FixtureSurface.WallOuter);
        deck.AddFixture(fixture);

        deck.RemoveFixture("conduit-1");

        Assert.DoesNotContain(fixture, deck.Fixtures);
    }

    [Fact]
    public void RemoveFixture_StopsPowerFromRoutingThroughIt()
    {
        var deck = new Deck();
        var source = new CellCoord(0, 0);
        var bridge = new CellCoord(1, 0);
        var target = new CellCoord(2, 0);
        deck.AddCell(source);
        deck.AddCell(bridge);
        deck.AddCell(target);
        deck.AddFixture(new MachineFixture("source", source, FixtureSurface.WallInner));
        deck.AddFixture(new ConduitFixture("bridge", bridge, FixtureSurface.FloorUnderside));
        deck.AddFixture(new MachineFixture("target", target, FixtureSurface.WallInner));

        var power = new PowerSystem(deck);
        power.MarkSource(new PowerNodeId("source"));

        Assert.True(power.IsPowered(new PowerNodeId("target")));

        deck.RemoveFixture("bridge");

        Assert.False(power.IsPowered(new PowerNodeId("target")));
    }

    [Fact]
    public void Ship_HoldsDecksAsAList_EvenWithOne()
    {
        var ship = new Ship();
        var deck = new Deck();

        ship.AddDeck(deck);

        Assert.Single(ship.Decks);
        Assert.Same(deck, ship.Decks[0]);
    }
}
