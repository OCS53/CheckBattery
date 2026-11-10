using CheckBattery.Core;

namespace CheckBattery.Tests;

public class CliAndPacerTests
{
    [Fact]
    public void Empty_args_show_help()
    {
        var result = CliParser.Parse(Array.Empty<string>());
        Assert.True(result.Ok);
        Assert.Equal(CliVerb.Help, result.Command!.Verb);
        Assert.Contains("demo", CliParser.Help(), StringComparison.Ordinal);
    }

    [Fact]
    public void Test_command_accepts_a_steady_load_and_keeps_the_floor_at_least_10()
    {
        var ok = CliParser.Parse(new[] { "test", "--minutes", "45", "--interval", "10", "--floor", "10", "--load", "steady", "--output", "out" });
        Assert.True(ok.Ok);
        Assert.Equal(CliVerb.Test, ok.Command!.Verb);
        Assert.Equal(45, ok.Command.Minutes);
        Assert.Equal(10, ok.Command.IntervalSeconds);
        Assert.Equal(10, ok.Command.FloorPercent);
        Assert.Equal(LoadLevel.Steady, ok.Command.Load);
        Assert.Equal("out", ok.Command.Output);

        var low = CliParser.Parse(new[] { "test", "--floor", "9" });
        Assert.False(low.Ok);
        Assert.Contains("10", low.Error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Flags_belong_to_one_command()
    {
        Assert.False(CliParser.Parse(new[] { "info", "--minutes", "5" }).Ok);
        Assert.False(CliParser.Parse(new[] { "monitor", "--load", "light" }).Ok);
        Assert.False(CliParser.Parse(new[] { "test", "--worn" }).Ok);
        Assert.False(CliParser.Parse(new[] { "demo", "--minutes", "5" }).Ok);
        Assert.False(CliParser.Parse(new[] { "test", "--load", "turbo" }).Ok);
        Assert.False(CliParser.Parse(new[] { "mystery" }).Ok);

        var demo = CliParser.Parse(new[] { "demo", "--worn" });
        Assert.True(demo.Ok);
        Assert.True(demo.Command!.WornDemo);
    }

    [Fact]
    public void Pacer_stops_for_a_low_battery_adapter_or_elapsed_time()
    {
        var battery = new BatteryReading { ChargePercent = 40, Discharging = true };
        var low = new BatteryReading { ChargePercent = 15, Discharging = true };
        var plugged = new BatteryReading { AcOnline = true, Charging = true, ChargePercent = 40 };

        Assert.Equal(SampleDecision.Continue, TestPacer.Decide(battery, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 15));
        Assert.Equal(SampleDecision.StopAtFloor, TestPacer.Decide(low, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 15));
        Assert.Equal(SampleDecision.PauseOnAc, TestPacer.Decide(plugged, TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(30), 15));
        Assert.Equal(SampleDecision.StopAtDuration, TestPacer.Decide(battery, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), 15));
    }
}
