using Scavengineers.Scripts.Player;

namespace Scavengineers.Scripts.Tests.Player;

public class SuitResourcesTests
{
    [Fact]
    public void Tick_DrainsO2_AtTheBaseRate_InBreathableAir()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21); // breathable, no extra exposure drain

        Assert.Equal(100f - 100f / 300f, suit.O2Percent, precision: 3);
        Assert.Equal(100f, suit.HealthPercent); // O2 isn't empty yet — Health untouched
    }

    [Fact]
    public void Tick_DrainsO2Faster_TheFurtherAmbientO2DropsBelowBreathable()
    {
        var suitInVacuum = new SuitResources();
        var suitInThinAir = new SuitResources();

        suitInVacuum.Tick(1.0, ambientO2Fraction: 0.0);
        suitInThinAir.Tick(1.0, ambientO2Fraction: 0.10);

        Assert.True(suitInVacuum.O2Percent < suitInThinAir.O2Percent);
        Assert.True(suitInThinAir.O2Percent < 100f - 100f / 300f); // still worse than the base-only drain
    }

    [Fact]
    public void Tick_DrainsO2Faster_WhenInSmoke()
    {
        var suitInSmoke = new SuitResources();
        var suitClearAir = new SuitResources();

        suitInSmoke.Tick(1.0, ambientO2Fraction: 0.21, inSmoke: true);
        suitClearAir.Tick(1.0, ambientO2Fraction: 0.21, inSmoke: false);

        Assert.True(suitInSmoke.O2Percent < suitClearAir.O2Percent);
        Assert.True(suitInSmoke.O2Percent < 100f - 100f / 300f); // worse than the base-only drain
    }

    [Fact]
    public void Tick_LeavesHealthAlone_WhileO2StillRemains()
    {
        var suit = new SuitResources();

        // Full vacuum for a while — O2 drops a lot but shouldn't hit exactly 0 this quickly.
        suit.Tick(1.0, ambientO2Fraction: 0.0);

        Assert.True(suit.O2Percent > 0f);
        Assert.Equal(100f, suit.HealthPercent);
    }

    [Fact]
    public void Tick_DrainsHealth_OnceO2IsFullyDepleted()
    {
        var suit = new SuitResources();
        suit.RestoreFrom(0f, 100f);

        suit.Tick(1.0, ambientO2Fraction: 0.0);

        Assert.Equal(0f, suit.O2Percent);
        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void RestoreFrom_ClampsBothValuesToZeroToOneHundred()
    {
        var suit = new SuitResources();

        suit.RestoreFrom(150f, -20f);

        Assert.Equal(100f, suit.O2Percent);
        Assert.Equal(0f, suit.HealthPercent);
    }
}
