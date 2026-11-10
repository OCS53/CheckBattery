using System.Text;
using CheckBattery.Core;

namespace CheckBattery.Tests;

public class WmiAndReportTests
{
    [Fact]
    public void Parser_converts_milliwatt_hours_and_discharge_rate()
    {
        var status = Row(
            ("RemainingCapacity", 36000u),
            ("Voltage", 11450u),
            ("DischargeRate", 15000u),
            ("Discharging", true),
            ("PowerOnline", 0u));
        var full = Row(("FullChargedCapacity", 72000u));
        var design = Row(
            ("DesignedCapacity", 80000u),
            ("ManufactureName", Encoding.Unicode.GetBytes("데모셀")),
            ("SerialNumber", "SN-1"));
        var cycles = Row(("CycleCount", 220u));
        var win32 = Row(
            ("Name", "내장 배터리"),
            ("Chemistry", 6u),
            ("EstimatedChargeRemaining", 10u),
            ("EstimatedRunTime", 71582788u),
            ("DesignVoltage", 11400u),
            ("BatteryStatus", 1u));

        var reading = WmiReadingParser.Parse(
            new[] { status },
            new[] { full },
            new[] { design },
            new[] { cycles },
            new[] { win32 },
            power: null,
            timestamp: new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(9)));

        Assert.True(reading.BatteryPresent);
        Assert.Equal(36, reading.RemainingCapacityWh);
        Assert.Equal(72, reading.FullChargeCapacityWh);
        Assert.Equal(80, reading.DesignCapacityWh);
        Assert.Equal(50, reading.ChargePercent);
        Assert.Equal(11.45, reading.VoltageVolts);
        Assert.Equal(-15, reading.PowerWatts);
        Assert.True(reading.Discharging);
        Assert.False(reading.AcOnline);
        Assert.Equal("리튬 이온", reading.Chemistry);
        Assert.Equal("데모셀", reading.Manufacturer);
        Assert.Equal("SN-1", reading.SerialNumber);
        Assert.Equal("내장 배터리", reading.Name);
        Assert.Equal(220, reading.CycleCount);
        Assert.Equal(11400, reading.DesignVoltageMillivolts);
        Assert.Equal(144, reading.EstimatedRuntimeMinutes);
    }

    [Fact]
    public void Parser_sums_dual_batteries_and_reads_charge_rate()
    {
        var first = Row(("RemainingCapacity", 20000u), ("ChargeRate", 30000u), ("Charging", true), ("PowerOnline", 1u));
        var second = Row(("RemainingCapacity", 10000u), ("ChargeRate", 15000u), ("Charging", true));
        var fulls = new[]
        {
            Row(("FullChargedCapacity", 40000u)),
            Row(("FullChargedCapacity", 20000u)),
        };

        var reading = WmiReadingParser.Parse(
            new[] { first, second },
            fulls,
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            power: null,
            timestamp: DateTimeOffset.UnixEpoch);

        Assert.Equal(30, reading.RemainingCapacityWh);
        Assert.Equal(60, reading.FullChargeCapacityWh);
        Assert.Equal(50, reading.ChargePercent);
        Assert.Equal(45, reading.PowerWatts);
        Assert.True(reading.Charging);
        Assert.True(reading.AcOnline);
        Assert.Equal(2, reading.BatteryCount);
    }

    [Fact]
    public void Parser_reports_no_battery_when_sources_are_empty()
    {
        var reading = WmiReadingParser.Parse(
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            Array.Empty<Dictionary<string, object?>>(),
            new SystemPowerSnapshot { BatteryPresent = false, AcOnline = true },
            DateTimeOffset.UnixEpoch);

        Assert.False(reading.BatteryPresent);
        Assert.Equal(0, reading.BatteryCount);
    }

    [Fact]
    public void Demo_healthy_pack_estimates_its_own_full_capacity()
    {
        var report = DischargeAnalyzer.Analyze(DemoScenario.Create(worn: false));

        Approx.Equal(90, report.HealthPercent);
        Assert.Equal("최상", report.HealthGrade);
        Approx.Equal(DemoScenario.HealthyFullWh, report.EstimatedUsableCapacityWh);
        Approx.Equal(1, report.SensorToGaugeRatio, 0.01);
        Assert.InRange(report.AverageDischargeWatts!.Value, 21.5, 22.5);
        Assert.False(report.PowerEstimatedFromGauge);
    }

    [Fact]
    public void Demo_worn_pack_is_flagged_for_replacement_watch()
    {
        var samples = DemoScenario.Create(worn: true);
        var report = DischargeAnalyzer.Analyze(samples);
        var text = ReportFormatter.Text(report, new ReportContext { Simulated = true, MachineName = "demo" });
        var html = ReportFormatter.Html(report, new ReportContext { Simulated = true, MachineName = "demo" });

        Approx.Equal(65, report.HealthPercent);
        Assert.Equal("주의", report.HealthGrade);
        Approx.Equal(DemoScenario.WornFullWh, report.EstimatedUsableCapacityWh);
        Assert.Contains("가상 데이터", text, StringComparison.Ordinal);
        Assert.Contains("주의", text, StringComparison.Ordinal);
        Assert.Contains("같습니다", text, StringComparison.Ordinal);
        Assert.Contains("관찰 시간: 40분", text, StringComparison.Ordinal);
        Assert.DoesNotContain("40분 0초", text, StringComparison.Ordinal);
        Assert.Contains("lang=\"ko\"", html, StringComparison.Ordinal);
        Assert.Contains("주의", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_escapes_battery_identity()
    {
        var samples = new[]
        {
            new BatteryReading
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                BatteryPresent = true,
                Discharging = true,
                PowerWatts = -10,
                RemainingCapacityWh = 40,
                FullChargeCapacityWh = 50,
                DesignCapacityWh = 60,
                ChargePercent = 80,
                Manufacturer = "A<B>",
            },
            new BatteryReading
            {
                Timestamp = DateTimeOffset.UnixEpoch.AddMinutes(30),
                BatteryPresent = true,
                Discharging = true,
                PowerWatts = -10,
                RemainingCapacityWh = 30,
                FullChargeCapacityWh = 50,
                DesignCapacityWh = 60,
                ChargePercent = 60,
                Manufacturer = "A<B>",
            },
        };

        var html = ReportFormatter.Html(DischargeAnalyzer.Analyze(samples), new ReportContext());
        Assert.Contains("A&lt;B&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("A<B>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_uses_invariant_numbers_and_a_header()
    {
        var csv = ReportFormatter.Csv(DemoScenario.Create(worn: false));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("timestamp,", lines[0], StringComparison.Ordinal);
        Assert.Equal(DemoScenario.Steps + 2, lines.Length);
        Assert.Contains(",0,0,1,", lines[1], StringComparison.Ordinal);
        var power = lines[1].Split(',')[6];
        Assert.DoesNotContain(",", power, StringComparison.Ordinal);
        Assert.Contains(".", power, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] pairs)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
            row[key] = value;
        return row;
    }
}
