using Scavengineers.Scripts.Player;

namespace Scavengineers.Scripts.Tests.Player;

public class SuitResourcesTests
{
    [Fact]
    public void Tick_DrainsO2AndPower_AtTheBaseRate_InBreathableAir()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21); // breathable, no extra exposure drain

        Assert.Equal(100f - 100f / 300f, suit.O2Percent, precision: 3);
        Assert.Equal(100f - 100f / 480f, suit.PowerPercent, precision: 3);
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
    public void Tick_DrainsPowerFaster_WhenFlashlightIsOn()
    {
        var suitWithFlashlight = new SuitResources();
        var suitWithoutFlashlight = new SuitResources();

        suitWithFlashlight.Tick(1.0, ambientO2Fraction: 0.21, flashlightOn: true);
        suitWithoutFlashlight.Tick(1.0, ambientO2Fraction: 0.21, flashlightOn: false);

        Assert.True(suitWithFlashlight.PowerPercent < suitWithoutFlashlight.PowerPercent);
    }

    [Fact]
    public void RestoreFrom_ClampsBothValuesToZeroToOneHundred()
    {
        var suit = new SuitResources();

        suit.RestoreFrom(150f, -20f);

        Assert.Equal(100f, suit.O2Percent);
        Assert.Equal(0f, suit.PowerPercent);
    }
}
