using CheckBattery.Core;

namespace CheckBattery.Tests;

public class BatteryMathTests
{
    [Theory]
    [InlineData(90, "최상")]
    [InlineData(89.9, "양호")]
    [InlineData(80, "양호")]
    [InlineData(79.9, "보통")]
    [InlineData(70, "보통")]
    [InlineData(69.9, "주의")]
    [InlineData(60, "주의")]
    [InlineData(59.9, "교체 권장")]
    public void Grade_follows_capacity_bands(double health, string grade) =>
        Assert.Equal(grade, BatteryMath.HealthGrade(health));

    [Fact]
    public void Unknown_health_has_its_own_grade() =>
        Assert.Equal("알 수 없음", BatteryMath.HealthGrade(null));

    [Fact]
    public void Health_is_full_charge_over_design()
    {
        Assert.Equal(90, BatteryMath.HealthPercent(64.8, 72)!.Value, 1);
        Assert.Null(BatteryMath.HealthPercent(10, 0));
        Assert.Null(BatteryMath.HealthPercent(null, 72));
    }

    [Fact]
    public void State_of_charge_prefers_percent_then_capacity_ratio()
    {
        var withPercent = Sample(percent: 42, remaining: 10, full: 50);
        Assert.Equal(42, BatteryMath.StateOfCharge(withPercent));

        var fromCapacity = Sample(percent: null, remaining: 25, full: 50);
        Assert.Equal(50, BatteryMath.StateOfCharge(fromCapacity));
    }

    [Fact]
    public void Discharge_watts_use_the_sign_and_the_discharging_flag()
    {
        Assert.Equal(12.5, BatteryMath.DischargeWatts(Sample(power: -12.5)));
        Assert.Equal(8, BatteryMath.DischargeWatts(Sample(power: 8, discharging: true)));
        Assert.Null(BatteryMath.DischargeWatts(Sample(power: 8, discharging: false)));
        Assert.Null(BatteryMath.DischargeWatts(Sample(power: null)));
    }

    private static BatteryReading Sample(double? percent = null, double? remaining = null, double? full = null, double? power = null, bool discharging = false) =>
        new()
        {
            ChargePercent = percent,
            RemainingCapacityWh = remaining,
            FullChargeCapacityWh = full,
            PowerWatts = power,
            Discharging = discharging,
        };
}
