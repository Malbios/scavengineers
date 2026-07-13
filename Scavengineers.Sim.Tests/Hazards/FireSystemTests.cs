using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Hazards;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Hazards;

public class FireSystemTests
{
    private const string GeneratorId = "generator";
    private const string ConduitId = "conduit";

    private static (Deck Deck, AtmosphereSystem Atmosphere, PowerSystem Power, CellCoord ConduitCell)
        BuildRig(float conduitCondition, bool powered, AtmosphereVolume initialVolume)
    {
        var generatorCell = new CellCoord(0, 0);
        var conduitCell = new CellCoord(1, 0);

        var deck = new Deck();
        deck.AddCell(generatorCell);
        deck.AddCell(conduitCell);
        deck.AddFixture(new MachineFixture(GeneratorId, generatorCell, FixtureSurface.WallInner));
        deck.AddFixture(new ConduitFixture(ConduitId, conduitCell, FixtureSurface.FloorUnderside) { Condition = conduitCondition });

        var atmosphere = new AtmosphereSystem(deck, initialVolume);

        var power = new PowerSystem(deck);
        if (powered)
        {
            power.MarkSource(new PowerNodeId(GeneratorId));
        }

        return (deck, atmosphere, power, conduitCell);
    }

    [Fact]
    public void PoweredDamagedAndOxygenated_Ignites()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(0.1f, powered: true, AtmosphereVolume.Breathable);
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);

        Assert.True(deck.IsOnFire(conduitCell));
    }

    [Fact]
    public void Unpowered_DoesNotIgnite()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(0.1f, powered: false, AtmosphereVolume.Breathable);
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);

        Assert.False(deck.IsOnFire(conduitCell));
    }

    [Fact]
    public void Undamaged_DoesNotIgnite()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(1.0f, powered: true, AtmosphereVolume.Breathable);
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);

        Assert.False(deck.IsOnFire(conduitCell));
    }

    [Fact]
    public void NoOxygen_DoesNotIgnite()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(0.1f, powered: true, AtmosphereVolume.Vacuum);
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);

        Assert.False(deck.IsOnFire(conduitCell));
    }

    [Fact]
    public void Burning_ConsumesOxygenOverTime()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(0.1f, powered: true, AtmosphereVolume.Breathable);
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);
        Assert.True(deck.IsOnFire(conduitCell));

        var o2Before = atmosphere.VolumeAt(conduitCell).O2Fraction;
        fire.Tick(1);

        Assert.True(atmosphere.VolumeAt(conduitCell).O2Fraction < o2Before);
    }

    [Fact]
    public void Burning_SelfExtinguishesOnceOxygenDepleted()
    {
        var (deck, atmosphere, power, conduitCell) = BuildRig(0.1f, powered: true, AtmosphereVolume.Breathable with { O2Fraction = 0.11 });
        var fire = new FireSystem(deck, atmosphere, power);

        fire.Tick(1);
        Assert.True(deck.IsOnFire(conduitCell));

        for (var i = 0; i < 20; i++)
        {
            fire.Tick(1);
        }

        Assert.False(deck.IsOnFire(conduitCell));
    }

    [Fact]
    public void BurningCell_DegradesAnAdjacentUnsealedConduitsConditionOverTime()
    {
        var burning = new CellCoord(0, 0);
        var neighbor = new CellCoord(1, 0);
        var deck = new Deck();
        deck.AddCell(burning);
        deck.AddCell(neighbor);
        var neighborConduit = new ConduitFixture("neighbor", neighbor, FixtureSurface.FloorUnderside);
        deck.AddFixture(neighborConduit);
        deck.IgniteFire(burning);

        var fire = new FireSystem(deck, new AtmosphereSystem(deck), new PowerSystem(deck));
        fire.Tick(1);

        Assert.True(neighborConduit.Condition < 1f);
    }

    [Fact]
    public void BurningCell_DoesNotDegradeAConduitAcrossASealedEdge()
    {
        var burning = new CellCoord(0, 0);
        var neighbor = new CellCoord(1, 0);
        var deck = new Deck();
        deck.AddCell(burning);
        deck.AddCell(neighbor);
        deck.SealEdge(burning, neighbor);
        var neighborConduit = new ConduitFixture("neighbor", neighbor, FixtureSurface.FloorUnderside);
        deck.AddFixture(neighborConduit);
        deck.IgniteFire(burning);

        var fire = new FireSystem(deck, new AtmosphereSystem(deck), new PowerSystem(deck));
        fire.Tick(1);

        Assert.Equal(1f, neighborConduit.Condition);
    }

    [Fact]
    public void HeatDamagedPoweredNeighbor_EventuallyIgnitesOnItsOwn()
    {
        // source is adjacent to neighbor directly (not through burning) so the power graph
        // connects without needing a fixture on the burning cell itself.
        var burning = new CellCoord(0, 0);
        var neighbor = new CellCoord(1, 0);
        var source = new CellCoord(2, 0);
        var deck = new Deck();
        deck.AddCell(burning);
        deck.AddCell(neighbor);
        deck.AddCell(source);
        deck.AddFixture(new MachineFixture("source", source, FixtureSurface.WallInner));
        var neighborConduit = new ConduitFixture("neighbor", neighbor, FixtureSurface.FloorUnderside);
        deck.AddFixture(neighborConduit);
        deck.IgniteFire(burning);

        var power = new PowerSystem(deck);
        power.MarkSource(new PowerNodeId("source"));
        var fire = new FireSystem(deck, new AtmosphereSystem(deck), power);

        // Checks ignition ever occurred during the run, not just the final state — the neighbor
        // can self-extinguish afterward from its own O2 consumption once burning (already covered
        // by Burning_SelfExtinguishesOnceOxygenDepleted), which isn't what this test is about.
        var everIgnited = false;
        for (var i = 0; i < 20 && !everIgnited; i++)
        {
            fire.Tick(1);
            everIgnited = deck.IsOnFire(neighbor);
        }

        Assert.True(everIgnited);
    }
}
