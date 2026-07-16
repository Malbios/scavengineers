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
    public void Tick_DrainsHealth_WhenCriticallyCold_EvenWithO2Remaining()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21, ambientTemperature: 50.0);

        Assert.True(suit.IsFreezing);
        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void Tick_DrainsHealth_WhenCriticallyHot()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21, ambientTemperature: 500.0);

        Assert.True(suit.IsBurning);
        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void Tick_DrainsHealthFaster_WhenO2DepletedAndFreezingTogether_ThanO2DepletionAlone()
    {
        var suitO2DepletedOnly = new SuitResources();
        suitO2DepletedOnly.RestoreFrom(0f, 100f);
        var suitO2DepletedAndFreezing = new SuitResources();
        suitO2DepletedAndFreezing.RestoreFrom(0f, 100f);

        suitO2DepletedOnly.Tick(1.0, ambientO2Fraction: 0.0, ambientTemperature: 293.15);
        suitO2DepletedAndFreezing.Tick(1.0, ambientO2Fraction: 0.0, ambientTemperature: 50.0);

        Assert.True(suitO2DepletedAndFreezing.HealthPercent < suitO2DepletedOnly.HealthPercent);
    }

    [Fact]
    public void RestoreFrom_ClampsBothValuesToZeroToOneHundred()
    {
        var suit = new SuitResources();

        suit.RestoreFrom(150f, -20f);

        Assert.Equal(100f, suit.O2Percent);
        Assert.Equal(0f, suit.HealthPercent);
    }

    [Fact]
    public void RestoreFrom_DefaultsCo2ToZero_ForOldTwoArgCalls()
    {
        var suit = new SuitResources();

        suit.RestoreFrom(80f, 90f); // old 2-arg shape — CO2 wasn't a thing yet

        Assert.Equal(0f, suit.CO2Percent);
    }

    [Fact]
    public void Tick_DoesNotDrainO2FromAmbient_WhileSealedWithAWorkingTank_EvenInVacuum()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.0, suitSealed: true, o2TankDepleted: false);

        Assert.Equal(100f, suit.O2Percent);
    }

    [Fact]
    public void Tick_FallsBackToTheAmbientFormula_OnceTheO2TankIsDepleted_EvenWhileSealed()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.0, suitSealed: true, o2TankDepleted: true);

        Assert.True(suit.O2Percent < 100f);
    }

    [Fact]
    public void Tick_FallsBackToTheAmbientFormula_WhenNotSealed_RegardlessOfTankState()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.0, suitSealed: false, o2TankDepleted: false);

        Assert.True(suit.O2Percent < 100f);
    }

    [Fact]
    public void Tick_BuildsUpCo2_OnlyWhileSealed()
    {
        var sealedSuit = new SuitResources();
        var unsealedSuit = new SuitResources();

        sealedSuit.Tick(1.0, suitSealed: true, filterDepleted: false);
        unsealedSuit.Tick(1.0, suitSealed: false);

        Assert.True(sealedSuit.CO2Percent > 0f);
        Assert.Equal(0f, unsealedSuit.CO2Percent);
    }

    [Fact]
    public void Tick_BuildsUpCo2Faster_OnceTheFilterIsDepleted()
    {
        var withFilter = new SuitResources();
        var withoutFilter = new SuitResources();

        withFilter.Tick(1.0, suitSealed: true, filterDepleted: false);
        withoutFilter.Tick(1.0, suitSealed: true, filterDepleted: true);

        Assert.True(withoutFilter.CO2Percent > withFilter.CO2Percent);
    }

    [Fact]
    public void Tick_VentsCo2BackDown_OnceUnsealed()
    {
        var suit = new SuitResources();
        suit.RestoreFrom(100f, 100f, co2Percent: 50f);

        suit.Tick(1.0, suitSealed: false);

        Assert.True(suit.CO2Percent < 50f);
    }

    [Fact]
    public void Tick_DrainsHealth_OnceCo2Reaches100()
    {
        var suit = new SuitResources();
        suit.RestoreFrom(100f, 100f, co2Percent: 100f);

        suit.Tick(1.0, suitSealed: true, filterDepleted: true);

        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void Tick_SuppressesColdHealthDrain_WhileSealedWithAChargedBattery()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21, ambientTemperature: 50.0, suitSealed: true,
            o2TankDepleted: false, n2TankDepleted: false, filterDepleted: false, batteryDepleted: false);

        Assert.True(suit.IsFreezing); // overlay still reflects real ambient temperature
        Assert.Equal(100f, suit.HealthPercent); // but the battery protected against the drain
    }

    [Fact]
    public void Tick_SuppressesBurnHealthDrain_WhileSealedWithAChargedBattery()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21, ambientTemperature: 500.0, suitSealed: true,
            o2TankDepleted: false, n2TankDepleted: false, filterDepleted: false, batteryDepleted: false);

        Assert.True(suit.IsBurning);
        Assert.Equal(100f, suit.HealthPercent);
    }

    [Fact]
    public void Tick_DrainsHealth_WhenSealedAndBatteryDepleted_EvenInAMildRoom()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.21, ambientTemperature: 293.15, suitSealed: true,
            o2TankDepleted: false, n2TankDepleted: false, filterDepleted: false, batteryDepleted: true);

        Assert.False(suit.IsFreezing);
        Assert.False(suit.IsBurning);
        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void Tick_DrainsHealth_WhenSealedAndN2Depleted()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, suitSealed: true, n2TankDepleted: true, o2TankDepleted: false, filterDepleted: false, batteryDepleted: false);

        Assert.True(suit.HealthPercent < 100f);
    }

    [Fact]
    public void Tick_LeavesHealthAlone_WhenSealedWithEverythingWorking()
    {
        var suit = new SuitResources();

        suit.Tick(1.0, ambientO2Fraction: 0.0, ambientTemperature: 293.15, suitSealed: true,
            o2TankDepleted: false, n2TankDepleted: false, filterDepleted: false, batteryDepleted: false);

        Assert.Equal(100f, suit.HealthPercent);
        Assert.Equal(100f, suit.O2Percent);
    }
}
