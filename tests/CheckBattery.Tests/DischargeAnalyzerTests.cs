using CheckBattery.Core;

namespace CheckBattery.Tests;

public class DischargeAnalyzerTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>샘플 간격이 길어도 절전으로 버리지 않는 시험 설정입니다.</summary>
    private static readonly DischargeAnalyzeOptions Wide = new() { MaxInterval = TimeSpan.FromHours(3) };

    [Fact]
    public void Constant_power_integrates_to_energy_and_scales_to_full_capacity()
    {
        var samples = new[]
        {
            Reading(0, watts: 20, remaining: 40, percent: 80),
            Reading(30, watts: 20, remaining: 30, percent: 60),
        };

        var report = DischargeAnalyzer.Analyze(samples, Wide);

        Assert.Equal(10, report.MeasuredEnergyWh, 2);
        Approx.Equal(20, report.AverageDischargeWatts);
        Approx.Equal(50, report.EstimatedUsableCapacityWh);
        Approx.Equal(50.0 / 60 * 100, report.HealthPercent);
        Assert.Equal("양호", report.HealthGrade);
        Approx.Equal(1, report.SensorToGaugeRatio);
        Approx.Equal(2.5, report.MeasuredRuntimeHours);
        Approx.Equal(2.5, report.GaugeRuntimeHours);
        Assert.False(report.PowerEstimatedFromGauge);
    }

    [Fact]
    public void Trapezoid_uses_the_average_power_of_each_interval()
    {
        var samples = new[]
        {
            Reading(0, watts: 10, remaining: 80, percent: 80),
            Reading(60, watts: 30, remaining: 60, percent: 60),
            Reading(120, watts: 30, remaining: 30, percent: 30),
        };

        var report = DischargeAnalyzer.Analyze(samples, Wide);

        Assert.Equal(50, report.MeasuredEnergyWh, 2);
        Assert.Equal(2, report.DischargeIntervals);
    }

    [Fact]
    public void Small_state_of_charge_drop_is_not_scaled()
    {
        var samples = new[]
        {
            Reading(0, watts: 10, remaining: 50, percent: 50),
            Reading(1, watts: 10, remaining: 49.8, percent: 49),
        };

        var report = DischargeAnalyzer.Analyze(samples);

        Assert.Null(report.EstimatedUsableCapacityWh);
        Assert.Contains(report.Warnings, warning => warning.Contains("환산하지 않았습니다", StringComparison.Ordinal));
    }

    [Fact]
    public void Adapter_intervals_are_left_out_of_the_integral()
    {
        var samples = new[]
        {
            Reading(0, watts: 10, remaining: 50),
            Reading(10, watts: 10, remaining: 50 - 10.0 * 10 / 60),
            Reading(20, watts: 0, remaining: 50 - 10.0 * 10 / 60, ac: true, charging: true),
            Reading(30, watts: 0, remaining: 50 - 10.0 * 10 / 60, ac: true, charging: true),
            Reading(40, watts: 10, remaining: 50 - 10.0 * 10 / 60),
            Reading(50, watts: 10, remaining: 50 - 10.0 * 20 / 60),
        };

        var report = DischargeAnalyzer.Analyze(samples, Wide);

        Assert.Equal(10.0 * 20 / 60, report.MeasuredEnergyWh, 2);
        Assert.Equal(3, report.ExcludedAcIntervals);
        Approx.Equal(50, report.EstimatedUsableCapacityWh, 0.2);
        Assert.Contains(report.Warnings, warning => warning.Contains("어댑터", StringComparison.Ordinal));
    }

    [Fact]
    public void Long_gap_is_treated_as_sleep()
    {
        var samples = new[]
        {
            Reading(0, watts: 30, remaining: 40, percent: 80),
            Reading(2, watts: 30, remaining: 39, percent: 78),
            Reading(40, watts: 30, remaining: 20, percent: 40),
        };

        var report = DischargeAnalyzer.Analyze(samples);

        Assert.Equal(1, report.ExcludedGapIntervals);
        Assert.Equal(30.0 * 2 / 60, report.MeasuredEnergyWh, 2);
    }

    [Fact]
    public void Implausible_power_spike_is_dropped()
    {
        var samples = new[]
        {
            Reading(0, watts: 10, remaining: 40, percent: 80),
            Reading(10, watts: 10, remaining: 38.3, percent: 76),
            Reading(20, watts: 900, remaining: 38, percent: 76),
            Reading(30, watts: 10, remaining: 36.3, percent: 72),
            Reading(40, watts: 10, remaining: 34.6, percent: 69),
        };

        var report = DischargeAnalyzer.Analyze(samples, Wide);

        Assert.Equal(2, report.IgnoredGlitchIntervals);
        Assert.Equal(10.0 * 20 / 60, report.MeasuredEnergyWh, 2);
    }

    [Fact]
    public void Missing_power_sensor_uses_the_gauge_drop()
    {
        var samples = new[]
        {
            Reading(0, watts: null, remaining: 40, percent: 100, full: 40, design: 50),
            Reading(6, watts: null, remaining: 38, percent: 95, full: 40, design: 50),
            Reading(12, watts: null, remaining: 36, percent: 90, full: 40, design: 50),
        };

        var report = DischargeAnalyzer.Analyze(samples, Wide);

        Assert.True(report.PowerEstimatedFromGauge);
        Assert.Equal(4, report.MeasuredEnergyWh, 2);
        Approx.Equal(40, report.EstimatedUsableCapacityWh);
        Assert.Null(report.SensorToGaugeRatio);
        Assert.Contains(report.Warnings, warning => warning.Contains("전력 센서", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_input_warns()
    {
        var report = DischargeAnalyzer.Analyze(Array.Empty<BatteryReading>());
        Assert.Contains(report.Warnings, warning => warning.Contains("샘플이 없습니다", StringComparison.Ordinal));
    }

    private static BatteryReading Reading(
        double minute,
        double? watts,
        double? remaining,
        double? percent = null,
        bool ac = false,
        bool charging = false,
        double full = 50,
        double design = 60)
    {
        return new BatteryReading
        {
            Timestamp = Origin.AddMinutes(minute),
            BatteryPresent = true,
            AcOnline = ac,
            Charging = charging,
            Discharging = !ac && !charging && watts is > 0,
            ChargePercent = percent,
            PowerWatts = watts is null ? null : -watts,
            RemainingCapacityWh = remaining,
            FullChargeCapacityWh = full,
            DesignCapacityWh = design,
            BatteryCount = 1,
            Name = "테스트 배터리",
            Manufacturer = "A<B>",
            Chemistry = "리튬 이온",
            CycleCount = 120,
        };
    }
}
