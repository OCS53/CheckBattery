namespace CheckBattery.Core;

public enum CliVerb
{
    Help,
    Info,
    Monitor,
    Test,
    Demo,
}

public sealed record CliCommand
{
    public required CliVerb Verb { get; init; }

    public int IntervalSeconds { get; init; }

    public int Minutes { get; init; }

    public int FloorPercent { get; init; }

    public LoadLevel Load { get; init; }

    public string? Output { get; init; }

    public bool WornDemo { get; init; }
}

public sealed record CliResult(bool Ok, string? Error, CliCommand? Command)
{
    public static CliResult Success(CliCommand command) => new(true, null, command);

    public static CliResult Failure(string error) => new(false, error, null);
}

public static class CliParser
{
    public const int DefaultMonitorIntervalSeconds = 2;
    public const int DefaultTestIntervalSeconds = 5;
    public const int DefaultTestMinutes = 30;
    public const int DefaultFloorPercent = 15;

    public static string Help() =>
        """
        CheckBattery — 노트북 배터리 성능 시험

        사용법
          CheckBattery info [--output 파일.txt]
          CheckBattery monitor [--interval 초] [--output 파일.csv]
          CheckBattery test [--minutes 분] [--interval 초] [--floor 퍼센트] [--load none|light|steady] [--output 폴더]
          CheckBattery demo [--worn] [--output 폴더]

        명령
          info      설계 용량, 완충 용량, 건강도, 지금 전력을 한 번 읽습니다.
          monitor   잔량과 충방전 전력을 계속 보여 줍니다. Ctrl+C 로 끝냅니다.
          test      어댑터를 뺀 뒤 방전을 기록하고, 사용한 에너지로 실사용 용량을 환산합니다.
          demo      가상 배터리로 성적서 모양을 만듭니다. Windows가 아니어도 동작합니다.

        시험 기본값
          30분, 5초 간격, 잔량 15%에서 중단, 부하 light.
          부하는 none(추가 없음), light(가벼운 CPU), steady(코어 절반을 고정 사용) 중 하나입니다.
          잔량 중단 값은 10에서 50 사이입니다. 10% 아래로는 내리지 않습니다.

        예시
          CheckBattery info
          CheckBattery test --minutes 45 --load steady
          CheckBattery demo --worn
        """;

    public static CliResult Parse(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
            return CliResult.Success(new CliCommand { Verb = CliVerb.Help });

        if (!TryVerb(args[0], out var verb))
            return CliResult.Failure($"알 수 없는 명령입니다: {args[0]}{Environment.NewLine}{Help()}");

        int? interval = null;
        int? minutes = null;
        int? floor = null;
        LoadLevel? load = null;
        string? output = null;
        var worn = false;

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (IsHelp(token))
                return CliResult.Success(new CliCommand { Verb = CliVerb.Help });

            switch (token)
            {
                case "--interval":
                    if (!TryTakeInt(args, ref i, out var seconds))
                        return CliResult.Failure("--interval 뒤에 초 단위 정수가 필요합니다.");
                    interval = seconds;
                    break;
                case "--minutes":
                    if (!TryTakeInt(args, ref i, out var takenMinutes))
                        return CliResult.Failure("--minutes 뒤에 분 단위 정수가 필요합니다.");
                    minutes = takenMinutes;
                    break;
                case "--floor":
                    if (!TryTakeInt(args, ref i, out var takenFloor))
                        return CliResult.Failure("--floor 뒤에 퍼센트 정수가 필요합니다.");
                    floor = takenFloor;
                    break;
                case "--load":
                    if (!TryTake(args, ref i, out var loadText) || !LoadLevelText.TryParse(loadText, out var parsedLoad))
                        return CliResult.Failure("--load 는 none, light, steady 중 하나여야 합니다.");
                    load = parsedLoad;
                    break;
                case "--output":
                    if (!TryTake(args, ref i, out var path))
                        return CliResult.Failure("--output 뒤에 파일 또는 폴더 경로가 필요합니다.");
                    output = path;
                    break;
                case "--worn":
                    worn = true;
                    break;
                default:
                    return CliResult.Failure($"알 수 없는 옵션입니다: {token}");
            }
        }

        var command = new CliCommand
        {
            Verb = verb,
            IntervalSeconds = interval ?? (verb == CliVerb.Monitor
                ? DefaultMonitorIntervalSeconds
                : DefaultTestIntervalSeconds),
            Minutes = minutes ?? DefaultTestMinutes,
            FloorPercent = floor ?? DefaultFloorPercent,
            Load = load ?? LoadLevel.Light,
            Output = output,
            WornDemo = worn,
        };

        return Validate(command, interval.HasValue, minutes.HasValue, floor.HasValue, load.HasValue, worn);
    }

    private static CliResult Validate(CliCommand command, bool hasInterval, bool hasMinutes, bool hasFloor, bool hasLoad, bool hasWorn)
    {
        switch (command.Verb)
        {
            case CliVerb.Info when hasInterval || hasMinutes || hasFloor || hasLoad || hasWorn:
                return CliResult.Failure("info 에서는 --output 만 쓸 수 있습니다.");
            case CliVerb.Monitor when hasMinutes || hasFloor || hasLoad || hasWorn:
                return CliResult.Failure("monitor 에서는 --interval 과 --output 만 쓸 수 있습니다.");
            case CliVerb.Test when hasWorn:
                return CliResult.Failure("test 에서는 --worn 을 쓸 수 없습니다. 가상 배터리는 demo 를 쓰세요.");
            case CliVerb.Demo when hasInterval || hasMinutes || hasFloor || hasLoad:
                return CliResult.Failure("demo 에서는 --worn 과 --output 만 쓸 수 있습니다.");
        }

        if (command.Verb is CliVerb.Monitor or CliVerb.Test && command.IntervalSeconds is < 1 or > 60)
            return CliResult.Failure("--interval 은 1초에서 60초 사이여야 합니다.");

        if (command.Verb == CliVerb.Test && command.Minutes is < 1 or > 720)
            return CliResult.Failure("--minutes 는 1분에서 720분 사이여야 합니다.");

        if (command.Verb == CliVerb.Test && command.FloorPercent is < 10 or > 50)
            return CliResult.Failure("배터리를 보호하기 위해 --floor 는 10에서 50 사이만 받습니다.");

        return CliResult.Success(command);
    }

    private static bool TryVerb(string text, out CliVerb verb)
    {
        switch (text.ToLowerInvariant())
        {
            case "info":
                verb = CliVerb.Info;
                return true;
            case "monitor":
                verb = CliVerb.Monitor;
                return true;
            case "test":
                verb = CliVerb.Test;
                return true;
            case "demo":
                verb = CliVerb.Demo;
                return true;
            case "help":
                verb = CliVerb.Help;
                return true;
            default:
                verb = CliVerb.Help;
                return false;
        }
    }

    private static bool IsHelp(string text) =>
        text is "-h" or "--help" or "/?" || string.Equals(text, "help", StringComparison.OrdinalIgnoreCase);

    private static bool TryTake(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = "";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryTakeInt(string[] args, ref int index, out int value)
    {
        if (!TryTake(args, ref index, out var text) || !int.TryParse(text, out value))
        {
            value = 0;
            return false;
        }

        return true;
    }
}
