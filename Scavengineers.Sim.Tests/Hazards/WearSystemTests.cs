using System.Linq;

using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Hazards;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Hazards;

public class WearSystemTests
{
    [Fact]
    public void Tick_DecaysAConduitFixturesCondition()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        deck.AddCell(cell);
        deck.AddFixture(new ConduitFixture("conduit", cell, FixtureSurface.FloorUnderside));
        var wear = new WearSystem(deck);

        wear.Tick(3600); // 1 hour

        var conduit = deck.Fixtures.Single(f => f.Id == "conduit");
        Assert.True(conduit.Condition < 1f);
    }

    [Fact]
    public void Tick_NeverDecaysABatteryFixture_ConditionMeansChargeThereNotWear()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        deck.AddCell(cell);
        deck.AddFixture(new BatteryFixture("battery", cell, FixtureSurface.WallInner));
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        var battery = deck.Fixtures.Single(f => f.Id == "battery");
        Assert.Equal(1f, battery.Condition);
    }

    [Fact]
    public void Tick_DecaysFloorAndCeilingHealth_ForEveryPresentCell()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        deck.AddCell(cell);
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        Assert.True(deck.FloorHealth(cell) < 1f);
        Assert.True(deck.CeilingHealth(cell) < 1f);
    }

    [Fact]
    public void Tick_DecaysWallHealth_ForASealedInteriorEdge()
    {
        var deck = new Deck();
        var a = new CellCoord(0, 0);
        var b = new CellCoord(1, 0);
        deck.AddCell(a);
        deck.AddCell(b);
        deck.SealEdge(a, b);
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        Assert.True(deck.WallHealth(a, b) < 1f);
    }

    [Fact]
    public void Tick_DecaysWallHealth_ForAnUnbreachedBoundaryEdge()
    {
        // A boundary edge (to a non-existent cell) counts as "walled" as long as it isn't
        // wall-edge-breached — same rule ShipBuildTarget.SeedDefaultShipLayout's own
        // MaybeSpawnWall already uses, mirrored here rather than requiring an explicit SealEdge
        // (which only ever applies to interior edges between two real cells).
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        var outside = new CellCoord(-1, 0);
        deck.AddCell(cell);
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        Assert.True(deck.WallHealth(cell, outside) < 1f);
    }

    [Fact]
    public void Tick_NeverDecaysWallHealth_ForABreachedBoundaryEdge_NothingLeftToDecay()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        var outside = new CellCoord(-1, 0);
        deck.AddCell(cell);
        deck.BreachWallEdge(cell, outside);
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        Assert.Equal(1f, deck.WallHealth(cell, outside));
    }

    [Fact]
    public void Tick_NeverDecaysFloorHealth_ForAnAlreadyBreachedFloor_NothingLeftToDecay()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        deck.AddCell(cell);
        deck.BreachHull(cell, StructuralSurface.Floor);
        var wear = new WearSystem(deck);

        wear.Tick(3600);

        Assert.Equal(1f, deck.FloorHealth(cell));
    }

    [Fact]
    public void Tick_EnoughElapsedTime_EventuallyBreachesTheFloorThroughPureDecay()
    {
        var deck = new Deck();
        var cell = new CellCoord(0, 0);
        deck.AddCell(cell);
        var wear = new WearSystem(deck);

        wear.Tick(10800); // full decay-to-zero baseline, see WearSystem's own DecayPerSecond doc

        Assert.True(deck.IsHullBreached(cell, StructuralSurface.Floor));
    }
}
