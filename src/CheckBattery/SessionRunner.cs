using System.Diagnostics;
using CheckBattery.Core;

namespace CheckBattery;

internal static class SessionRunner
{
    public static async Task<IReadOnlyList<BatteryReading>> MonitorAsync(
        IBatteryProbe probe,
        TimeSpan interval,
        CancellationToken cancellationToken,
        Action<string> write,
        Action<BatteryReading>? onSample = null)
    {
        var samples = new List<BatteryReading>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            if (!TryRead(probe, write, out var sample))
                break;

            samples.Add(sample);
            write(ReportFormatter.MonitorLine(sample));
            onSample?.Invoke(sample);

            if (!await DelayRemainingAsync(interval, started.Elapsed, cancellationToken))
                break;
        }

        return samples;
    }

    public static async Task<IReadOnlyList<BatteryReading>> TestAsync(
        IBatteryProbe probe,
        TimeSpan planned,
        TimeSpan interval,
        double floorPercent,
        CancellationToken cancellationToken,
        Action<string> write)
    {
        var samples = new List<BatteryReading>();
        var elapsedOnBattery = TimeSpan.Zero;
        DateTimeOffset? previousBatteryStamp = null;
        var paused = false;
        var wall = Stopwatch.StartNew();
        var wallCap = planned + planned + TimeSpan.FromMinutes(30);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (wall.Elapsed > wallCap)
            {
                write("기다리는 시간이 길어 시험을 마칩니다.");
                break;
            }

            var started = Stopwatch.StartNew();
            if (!TryRead(probe, write, out var sample))
                break;

            samples.Add(sample);
            var partial = DischargeAnalyzer.Analyze(samples);
            write($"{ReportFormatter.MonitorLine(sample)}  적산 {partial.MeasuredEnergyWh:0.00} Wh");

            var projected = elapsedOnBattery;
            if (previousBatteryStamp is DateTimeOffset previous && !sample.AcOnline && !sample.Charging)
                projected += sample.Timestamp - previous;

            switch (TestPacer.Decide(sample, projected, planned, floorPercent))
            {
                case SampleDecision.PauseOnAc:
                    if (!paused)
                        write("어댑터가 연결되었습니다. 방전 측정을 잠시 멈춥니다. 분리하면 이어서 잽니다.");
                    paused = true;
                    previousBatteryStamp = null;
                    break;
                case SampleDecision.StopAtFloor:
                    elapsedOnBattery = projected;
                    write($"잔량이 {floorPercent:0}%에 도달해 시험을 마칩니다.");
                    return samples;
                case SampleDecision.StopAtDuration:
                    elapsedOnBattery = projected;
                    previousBatteryStamp = sample.Timestamp;
                    if (samples.Count > 1)
                    {
                        write("예정한 시간이 되어 시험을 마칩니다.");
                        return samples;
                    }

                    break;
                default:
                    if (paused)
                        write("어댑터가 분리되었습니다. 방전 측정을 다시 시작합니다.");
                    paused = false;
                    elapsedOnBattery = projected;
                    previousBatteryStamp = sample.Timestamp;
                    break;
            }

            if (!await DelayRemainingAsync(interval, started.Elapsed, cancellationToken))
                break;
        }

        return samples;
    }

    private static bool TryRead(IBatteryProbe probe, Action<string> write, out BatteryReading sample)
    {
        try
        {
            sample = probe.Read();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            write("배터리를 읽지 못했습니다: " + ex.Message);
            sample = new BatteryReading();
            return false;
        }
    }

    private static async Task<bool> DelayRemainingAsync(TimeSpan interval, TimeSpan spent, CancellationToken cancellationToken)
    {
        var remaining = interval - spent;
        if (remaining <= TimeSpan.Zero)
            return true;

        try
        {
            await Task.Delay(remaining, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
