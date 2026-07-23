using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Fleet;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Tests.Fleet;

/// <summary>Covers the two things ShipSystems exists for: that a ship can simulate with no scene at
/// all (every test here builds one out of plain objects — there is no Godot anywhere), and that the
/// coarse level-of-detail tick an absent ship uses is a pacing change, not a discount.</summary>
public class ShipSystemsTests
{
    private static Deck ThreeByThreeDeck()
    {
        var deck = new Deck();
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                deck.AddCell(new CellCoord(x, y));
            }
        }

        return deck;
    }

    [Fact]
    public void CoarseTick_BanksTimeAndSpendsItWhole_MatchingAFullFidelityShipOverTheSameSpan()
    {
        var present = new ShipSystems(ThreeByThreeDeck(), hasLifeSupport: false);
        var absent = new ShipSystems(ThreeByThreeDeck(), hasLifeSupport: false);

        // Two seconds of simulated time: 120 frames at 1/60 for the present ship, the same 120
        // frames banked into 2 coarse ticks for the absent one.
        for (var frame = 0; frame < 120; frame++)
        {
            present.Tick(1.0 / 60.0);
            absent.TickCoarse(1.0 / 60.0);
        }

        var cell = new CellCoord(0, 0);

        // Compared as a fraction of the decay, not to N decimal places: float rounding means 120
        // small subtractions won't land bit-identically on 2 large ones, but that's noise — a
        // real discount would show up as a whole-percent gap, which this still catches.
        var presentDecay = 1f - present.Deck.FloorHealth(cell);
        var absentDecay = 1f - absent.Deck.FloorHealth(cell);

        Assert.True(presentDecay > 0, "the present ship should have worn measurably in two seconds");
        Assert.True(
            Math.Abs(presentDecay - absentDecay) / presentDecay < 0.01f,
            $"coarse-ticked decay {absentDecay} differs from full-fidelity {presentDecay} by more than 1%");
    }

    [Fact]
    public void CoarseTick_DoesNothingUntilAFullLumpHasAccumulated()
    {
        var systems = new ShipSystems(ThreeByThreeDeck(), hasLifeSupport: false);
        var cell = new CellCoord(0, 0);

        for (var frame = 0; frame < 30; frame++) // half a second
        {
            systems.TickCoarse(1.0 / 60.0);
        }

        Assert.Equal(1f, systems.Deck.FloorHealth(cell));
    }

    [Fact]
    public void AbsentShipWithABreach_StillVents_SoALeftDerelictIsStillVentedOnReturn()
    {
        var deck = ThreeByThreeDeck();
        deck.BreachWallEdge(new CellCoord(0, 0), new CellCoord(-1, 0));
        var systems = new ShipSystems(deck, hasLifeSupport: false);

        for (var frame = 0; frame < 180; frame++) // three seconds
        {
            systems.TickCoarse(1.0 / 60.0);
        }

        Assert.True(systems.Atmosphere.VolumeAt(new CellCoord(2, 2)).O2Fraction < 0.01);
    }

    [Fact]
    public void CaptureAndApplyVolumes_RoundTripEveryCell()
    {
        var deck = ThreeByThreeDeck();
        var source = new ShipSystems(deck, hasLifeSupport: false);
        source.Atmosphere.ApplyExternalVolume(new CellCoord(1, 1), AtmosphereVolume.Vacuum);

        var captured = source.CaptureVolumes().ToList();

        var restored = new ShipSystems(ThreeByThreeDeck(), hasLifeSupport: false);
        restored.ApplyVolumes(captured);

        Assert.Equal(0, restored.Atmosphere.VolumeAt(new CellCoord(1, 1)).O2Fraction);
        Assert.Equal(AtmosphereVolume.Breathable.O2Fraction, restored.Atmosphere.VolumeAt(new CellCoord(0, 0)).O2Fraction);
    }

    [Fact]
    public void ApplyVolumes_IgnoresCellsThisDeckNoLongerHas()
    {
        var systems = new ShipSystems(ThreeByThreeDeck(), hasLifeSupport: false);

        // A save written when the ship was bigger — must degrade quietly, not throw, per the
        // save-schema doc's missing-ID rule.
        systems.ApplyVolumes([(new CellCoord(99, 99), AtmosphereVolume.Vacuum)]);

        Assert.Equal(AtmosphereVolume.Breathable.O2Fraction, systems.Atmosphere.VolumeAt(new CellCoord(0, 0)).O2Fraction);
    }

    [Fact]
    public void ApplyFires_ReplacesWholesale_SoASaveWithNoFiresPutsABurningShipOut()
    {
        var deck = ThreeByThreeDeck();
        var systems = new ShipSystems(deck, hasLifeSupport: false);
        deck.IgniteFire(new CellCoord(0, 0));
        deck.IgniteFire(new CellCoord(1, 1));

        systems.ApplyFires([new CellCoord(2, 2)]);

        Assert.False(deck.IsOnFire(new CellCoord(0, 0)));
        Assert.False(deck.IsOnFire(new CellCoord(1, 1)));
        Assert.True(deck.IsOnFire(new CellCoord(2, 2)));

        systems.ApplyFires([]);
        Assert.Empty(deck.Fires);
    }
}
