using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CheckBattery.Core;

namespace CheckBattery;

internal static class Program
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // 출력을 리다이렉트한 환경에서는 인코딩을 바꾸지 않아도 됩니다.
        }

        var parsed = CliParser.Parse(args);
        if (!parsed.Ok || parsed.Command is null)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        try
        {
            return parsed.Command.Verb switch
            {
                CliVerb.Help => PrintHelp(),
                CliVerb.Demo => RunDemo(parsed.Command),
                CliVerb.Info => RunInfo(parsed.Command),
                CliVerb.Monitor => await RunMonitorAsync(parsed.Command),
                CliVerb.Test => await RunTestAsync(parsed.Command),
                _ => PrintHelp(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("오류: " + ex.Message);
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine(CliParser.Help());
        return 0;
    }

    private static int RunDemo(CliCommand command)
    {
        var samples = DemoScenario.Create(command.WornDemo);
        var report = DischargeAnalyzer.Analyze(samples);
        var context = MakeContext(command, simulated: true);
        Console.WriteLine(ReportFormatter.Text(report, context));
        PrintSaved(SaveDischarge(command.Output, report, context, samples));
        return 0;
    }

    private static int RunInfo(CliCommand command)
    {
        if (!RequireWindows())
            return 2;

        var reading = ProbeFactory.Create().Read();
        var text = ReportFormatter.Status(reading);
        Console.WriteLine(text);
        if (!string.IsNullOrWhiteSpace(command.Output))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(command.Output));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(command.Output, text, Utf8Bom);
            Console.WriteLine("저장: " + Path.GetFullPath(command.Output));
        }

        return reading.BatteryPresent ? 0 : 2;
    }

    private static async Task<int> RunMonitorAsync(CliCommand command)
    {
        if (!RequireWindows())
            return 2;

        var probe = ProbeFactory.Create();
        var first = probe.Read();
        Console.WriteLine(ReportFormatter.Status(first));
        if (!first.BatteryPresent)
            return 2;

        StreamWriter? csv = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(command.Output))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(command.Output));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                csv = new StreamWriter(command.Output, append: false, Utf8Bom);
                csv.WriteLine(ReportFormatter.CsvHeader);
                csv.Flush();
            }

            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancel.Cancel();
            };

            Console.WriteLine("모니터링을 시작합니다. 멈추려면 Ctrl+C 를 누르세요.");
            using var awake = new KeepAwake();
            await SessionRunner.MonitorAsync(
                probe,
                TimeSpan.FromSeconds(command.IntervalSeconds),
                cancel.Token,
                Console.WriteLine,
                sample =>
                {
                    if (csv is null)
                        return;
                    csv.WriteLine(ReportFormatter.CsvRow(sample));
                    csv.Flush();
                });
            return 0;
        }
        finally
        {
            if (csv is not null)
            {
                await csv.DisposeAsync();
                Console.WriteLine("저장: " + Path.GetFullPath(command.Output!));
            }
        }
    }

    private static async Task<int> RunTestAsync(CliCommand command)
    {
        if (!RequireWindows())
            return 2;

        var probe = ProbeFactory.Create();
        var first = probe.Read();
        Console.WriteLine(ReportFormatter.Status(first));
        if (!first.BatteryPresent)
            return 2;
        if (first.AcOnline || first.Charging)
        {
            Console.Error.WriteLine("어댑터가 연결되어 있습니다. 분리한 다음 다시 실행하세요. 충전 중에는 방전량을 잴 수 없습니다.");
            return 2;
        }

        if (BatteryMath.StateOfCharge(first) is double startPercent && startPercent <= command.FloorPercent)
        {
            Console.Error.WriteLine($"잔량이 {command.FloorPercent}% 이하입니다. 이 상태에서는 방전 시험을 시작하지 않습니다.");
            return 2;
        }

        if (BatteryMath.StateOfCharge(first) is not double)
            Console.WriteLine("잔량 퍼센트를 읽지 못했습니다. 예정 시간이 되면 시험을 마칩니다.");
        else if (BatteryMath.StateOfCharge(first) is double percent && percent < command.FloorPercent + 5)
            Console.WriteLine("시작 잔량과 중단 잔량의 차이가 작습니다. 환산 용량은 나오지 않을 수 있습니다.");

        Console.WriteLine(LoadLevelText.Describe(command.Load));
        Console.WriteLine($"시험을 시작합니다. {command.Minutes}분 또는 잔량 {command.FloorPercent}%에서 멈춥니다. 멈추려면 Ctrl+C 를 누르세요.");
        Console.WriteLine("노트북을 딱딱한 바닥에 두고, 너무 뜨거워지면 Ctrl+C 로 중단하세요.");

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancel.Cancel();
        };

        IReadOnlyList<BatteryReading> samples;
        using (new KeepAwake())
        using (new CpuWorkload(command.Load))
        {
            samples = await SessionRunner.TestAsync(
                probe,
                TimeSpan.FromMinutes(command.Minutes),
                TimeSpan.FromSeconds(command.IntervalSeconds),
                command.FloorPercent,
                cancel.Token,
                Console.WriteLine);
        }

        if (samples.Count == 0)
        {
            Console.Error.WriteLine("저장할 측정값이 없습니다.");
            return 1;
        }

        var report = DischargeAnalyzer.Analyze(samples);
        var context = MakeContext(command, simulated: false);
        Console.WriteLine();
        Console.WriteLine(ReportFormatter.Text(report, context));
        PrintSaved(SaveDischarge(command.Output, report, context, samples));
        return 0;
    }

    private static ReportContext MakeContext(CliCommand command, bool simulated)
    {
        var notes = new List<string>();
        if (simulated)
        {
            notes.Add(command.WornDemo
                ? "가상 배터리는 설계 72Wh, 완충 46.8Wh(건강도 65%)이고 약 22W로 40분간 방전합니다."
                : "가상 배터리는 설계 72Wh, 완충 64.8Wh(건강도 90%)이고 약 22W로 40분간 방전합니다.");
        }
        else
        {
            notes.Add($"기록 간격은 {command.IntervalSeconds}초이고, 잔량 {command.FloorPercent}%에서 멈춥니다.");
            notes.Add("시험 중에는 절전과 화면 꺼짐을 막습니다. 사용 시간에는 화면이 켜진 전력이 포함됩니다.");
        }

        return new ReportContext
        {
            LoadDescription = simulated ? "가상 방전 약 22W" : LoadLevelText.Describe(command.Load),
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            Simulated = simulated,
            ExtraNotes = notes,
        };
    }

    private static string SaveDischarge(
        string? output,
        DischargeReport report,
        ReportContext context,
        IReadOnlyList<BatteryReading> samples)
    {
        var directory = string.IsNullOrWhiteSpace(output) ? "reports" : output;
        Directory.CreateDirectory(directory);
        var stamp = report.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, "battery-" + stamp);
        var suffix = 2;
        while (File.Exists(path + ".txt"))
        {
            path = Path.Combine(directory, "battery-" + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        File.WriteAllText(path + ".txt", ReportFormatter.Text(report, context), Utf8Bom);
        File.WriteAllText(path + ".html", ReportFormatter.Html(report, context), Utf8Bom);
        File.WriteAllText(path + ".csv", ReportFormatter.Csv(samples), Utf8Bom);
        return path;
    }

    private static void PrintSaved(string path)
    {
        Console.WriteLine();
        Console.WriteLine("저장: " + Path.GetFullPath(path + ".txt"));
        Console.WriteLine("저장: " + Path.GetFullPath(path + ".html"));
        Console.WriteLine("저장: " + Path.GetFullPath(path + ".csv"));
    }

    private static bool RequireWindows()
    {
        if (OperatingSystem.IsWindows())
            return true;

        Console.Error.WriteLine("이 명령은 Windows 노트북에서만 동작합니다.");
        Console.Error.WriteLine("성적서 예시는 CheckBattery demo 로 볼 수 있습니다.");
        return false;
    }
}
