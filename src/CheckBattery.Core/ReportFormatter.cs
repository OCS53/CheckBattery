using System.Globalization;
using System.Net;
using System.Text;

namespace CheckBattery.Core;

public sealed record ReportContext
{
    public string LoadDescription { get; init; } = "추가 부하 없음";

    public string MachineName { get; init; } = "";

    public string OsDescription { get; init; } = "";

    public bool Simulated { get; init; }

    public IReadOnlyList<string> ExtraNotes { get; init; } = [];
}

public static class ReportFormatter
{
    public static string Status(BatteryReading reading)
    {
        var builder = new StringBuilder();
        builder.AppendLine("노트북 배터리 상태");
        builder.AppendLine(new string('─', 28));
        if (!reading.BatteryPresent)
        {
            builder.AppendLine("배터리를 찾지 못했습니다. 데스크톱이거나 펌웨어가 배터리 정보를 주지 않습니다.");
            return builder.ToString();
        }

        var health = BatteryMath.HealthPercent(reading.FullChargeCapacityWh, reading.DesignCapacityWh);
        AppendField(builder, "이름", BatteryLabel(reading));
        AppendField(builder, "제조사", reading.Manufacturer);
        AppendField(builder, "화학", reading.Chemistry);
        AppendField(builder, "일련번호", reading.SerialNumber);
        AppendField(builder, "설계 용량", Wh(reading.DesignCapacityWh));
        AppendField(builder, "완충 용량", Wh(reading.FullChargeCapacityWh));
        AppendField(builder, "현재 잔량", Remaining(reading));
        AppendField(builder, "건강도", health is null ? "알 수 없음" : $"{health:0.0}% ({BatteryMath.HealthGrade(health)})");
        AppendField(builder, "사이클", reading.CycleCount is int cycles ? $"{cycles}회. {BatteryMath.CycleComment(cycles)}" : BatteryMath.CycleComment(null));
        AppendField(builder, "전압", Volts(reading.VoltageVolts));
        AppendField(builder, "설계 전압", reading.DesignVoltageMillivolts is int mv ? $"{mv / 1000.0:0.00} V" : null);
        AppendField(builder, "전력", Power(reading));
        AppendField(builder, "전원", PowerSource(reading));
        AppendField(builder, "예상 시간", Runtime(reading));
        builder.AppendLine();
        builder.AppendLine("건강도는 컨트롤러가 보고한 완충 용량을 설계 용량으로 나눈 값입니다.");
        return builder.ToString();
    }

    public static string Text(DischargeReport report, ReportContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("배터리 성능 시험 성적서");
        builder.AppendLine(new string('=', 32));
        if (context.Simulated)
            builder.AppendLine("이 성적서는 가상 데이터로 만든 예시이며 실제 배터리가 아닙니다.");
        AppendField(builder, "컴퓨터", context.MachineName);
        AppendField(builder, "운영체제", context.OsDescription);
        AppendField(builder, "부하", context.LoadDescription);
        AppendField(builder, "시작", When(report.StartedAt));
        AppendField(builder, "종료", When(report.EndedAt));
        AppendField(builder, "관찰 시간", FormatDuration(report.ObservedDuration));
        AppendField(builder, "방전 시간", FormatDuration(report.DischargeDuration));
        AppendField(builder, "샘플", $"{report.SampleCount}개, 방전 구간 {report.DischargeIntervals}개");
        builder.AppendLine();
        builder.AppendLine("배터리");
        builder.AppendLine("------");
        AppendField(builder, "이름", report.BatteryName);
        AppendField(builder, "제조사", report.Manufacturer);
        AppendField(builder, "화학", report.Chemistry);
        AppendField(builder, "일련번호", report.SerialNumber);
        AppendField(builder, "개수", report.BatteryCount > 0 ? report.BatteryCount.ToString(CultureInfo.InvariantCulture) : null);
        AppendField(builder, "설계 용량", Wh(report.DesignCapacityWh));
        AppendField(builder, "완충 용량", Wh(report.GaugeFullChargeCapacityWh));
        AppendField(builder, "건강도", report.HealthPercent is double health ? $"{health:0.0}% ({report.HealthGrade})" : "알 수 없음");
        AppendField(builder, "사이클", report.CycleCount is int cycles ? $"{cycles}회. {BatteryMath.CycleComment(cycles)}" : null);
        AppendField(builder, "마지막 전압", Volts(report.LastVoltageVolts));
        builder.AppendLine();
        builder.AppendLine("측정");
        builder.AppendLine("----");
        AppendField(builder, "시작 잔량", Percent(report.StartPercent));
        AppendField(builder, "종료 잔량", Percent(report.EndPercent));
        AppendField(builder, "평균 전력", Watts(report.AverageDischargeWatts));
        AppendField(builder, "사용 에너지", Wh(report.MeasuredEnergyWh));
        AppendField(builder, "게이지 감소", Wh(report.GaugeDropWh));
        AppendField(builder, "센서/게이지", report.SensorToGaugeRatio is double ratio ? $"{ratio:0.00}" : "비교 안 함");
        AppendField(builder, "환산 용량", Wh(report.EstimatedUsableCapacityWh));
        AppendField(builder, "이 부하로 완충에서 방전 끝까지", Hours(report.MeasuredRuntimeHours));
        AppendField(builder, "게이지 완충 용량 기준", Hours(report.GaugeRuntimeHours));
        builder.AppendLine();
        builder.AppendLine("해석");
        builder.AppendLine("----");
        foreach (var line in Interpret(report))
            builder.AppendLine(line);
        builder.AppendLine();
        builder.AppendLine("계산");
        builder.AppendLine("----");
        foreach (var line in report.Notes)
            builder.AppendLine(line);
        foreach (var line in context.ExtraNotes)
            builder.AppendLine(line);
        builder.AppendLine();
        builder.AppendLine("주의");
        builder.AppendLine("----");
        if (report.Warnings.Count == 0)
            builder.AppendLine("없음");
        else
        {
            foreach (var line in report.Warnings)
                builder.AppendLine("- " + line);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> Interpret(DischargeReport report)
    {
        var lines = new List<string>();
        if (report.HealthPercent is double health)
        {
            lines.Add($"설계 용량 대비 완충 용량은 {health:0.0}%이고, 상태는 '{report.HealthGrade}'입니다.");
        }
        else
        {
            lines.Add("설계 용량 또는 완충 용량을 읽지 못해 건강도를 계산하지 못했습니다.");
        }

        if (report.CycleCount is int cycles)
            lines.Add($"사이클은 {cycles}회입니다. {BatteryMath.CycleComment(cycles)}");

        if (report.EstimatedUsableCapacityWh is double estimated && report.GaugeFullChargeCapacityWh is double gauge && gauge > 0)
        {
            var diff = (estimated - gauge) / gauge * 100.0;
            var comparison = Math.Abs(diff) < 0.05
                ? $"컨트롤러 완충 용량 {gauge:0.00} Wh와 같습니다."
                : diff > 0
                    ? $"컨트롤러 완충 용량 {gauge:0.00} Wh보다 {diff:0.0}% 큽니다."
                    : $"컨트롤러 완충 용량 {gauge:0.00} Wh보다 {-diff:0.0}% 작습니다.";
            lines.Add($"이번 방전으로 환산한 실사용 용량은 {estimated:0.00} Wh입니다. {comparison}");
        }
        else if (report.EstimatedUsableCapacityWh is double estimatedOnly)
        {
            lines.Add($"이번 방전으로 환산한 실사용 용량은 {estimatedOnly:0.00} Wh입니다.");
        }
        else
        {
            lines.Add("잔량이 충분히 줄지 않아 실사용 용량으로 환산하지 않았습니다. 5퍼센트포인트 이상 방전하면 환산할 수 있습니다.");
        }

        if (report.AverageDischargeWatts is double watts)
        {
            lines.Add($"방전 구간 평균 전력은 {watts:0.00} W입니다.");
            if (report.MeasuredRuntimeHours is double measuredHours)
                lines.Add($"이 전력이 유지되면 환산 용량 기준으로 약 {FormatHours(measuredHours)} 쓸 수 있습니다.");
            else if (report.GaugeRuntimeHours is double gaugeHours)
                lines.Add($"이 전력이 유지되면 컨트롤러 완충 용량 기준으로 약 {FormatHours(gaugeHours)} 쓸 수 있습니다.");
        }

        if (report.SensorToGaugeRatio is double ratio)
        {
            lines.Add(ratio is >= 0.95 and <= 1.05
                ? $"전력 센서와 잔량 게이지의 비는 {ratio:0.00}이라 서로 잘 맞습니다."
                : $"전력 센서와 잔량 게이지의 비는 {ratio:0.00}입니다. 1에서 멀수록 둘 중 하나의 오차가 큽니다.");
        }
        else if (report.PowerEstimatedFromGauge)
        {
            lines.Add("이번 에너지는 잔량 게이지에서 나왔으므로 센서와 대조하지 않았습니다.");
        }

        lines.Add("사용 시간은 시험 중 부하와 화면이 켜진 상태가 계속된다고 본 값입니다. 밝기, 무선랜, 실행 중인 프로그램에 따라 달라집니다.");
        return lines;
    }

    public static string Html(DischargeReport report, ReportContext context)
    {
        var gradeClass = GradeClass(report.HealthGrade);
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ko\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("<title>배터리 성능 시험 성적서</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(Css);
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("<h1>배터리 성능 시험 성적서</h1>");
        builder.AppendLine($"<p class=\"sub\">{E(When(report.StartedAt))} – {E(When(report.EndedAt))}</p>");
        if (context.Simulated)
            builder.AppendLine("<p class=\"banner\">이 성적서는 가상 데이터로 만든 예시이며 실제 배터리가 아닙니다.</p>");

        builder.AppendLine("<section class=\"cards\">");
        Card(builder, "건강도", report.HealthPercent is double health ? $"{health:0.0}%" : "없음", report.HealthGrade, gradeClass);
        Card(builder, "환산 용량", report.EstimatedUsableCapacityWh is double estimated ? $"{estimated:0.00} Wh" : "범위 부족", "실측 에너지 기준", "");
        Card(builder, "평균 전력", report.AverageDischargeWatts is double watts ? $"{watts:0.00} W" : "없음", "방전 구간", "");
        Card(builder, "예상 사용 시간", Hours(report.MeasuredRuntimeHours ?? report.GaugeRuntimeHours), "이번 부하가 계속될 때", "");
        builder.AppendLine("</section>");

        builder.AppendLine("<h2>용량 비교</h2>");
        builder.AppendLine(CapacityMeters(report));

        builder.AppendLine("<h2>배터리</h2>");
        builder.AppendLine("<table>");
        Row(builder, "이름", report.BatteryName);
        Row(builder, "제조사", report.Manufacturer);
        Row(builder, "화학", report.Chemistry);
        Row(builder, "일련번호", report.SerialNumber);
        Row(builder, "개수", report.BatteryCount > 0 ? report.BatteryCount.ToString(CultureInfo.InvariantCulture) : null);
        Row(builder, "설계 용량", Wh(report.DesignCapacityWh));
        Row(builder, "완충 용량", Wh(report.GaugeFullChargeCapacityWh));
        Row(builder, "건강도", report.HealthPercent is double h ? $"{h:0.0}% ({report.HealthGrade})" : "알 수 없음");
        Row(builder, "사이클", report.CycleCount is int cycles ? $"{cycles}회. {BatteryMath.CycleComment(cycles)}" : null);
        Row(builder, "컴퓨터", context.MachineName);
        Row(builder, "운영체제", context.OsDescription);
        Row(builder, "부하", context.LoadDescription);
        builder.AppendLine("</table>");

        builder.AppendLine("<h2>측정</h2>");
        builder.AppendLine("<table>");
        Row(builder, "관찰 시간", FormatDuration(report.ObservedDuration));
        Row(builder, "방전 시간", FormatDuration(report.DischargeDuration));
        Row(builder, "샘플", $"{report.SampleCount}개");
        Row(builder, "시작 잔량", Percent(report.StartPercent));
        Row(builder, "종료 잔량", Percent(report.EndPercent));
        Row(builder, "사용 에너지", Wh(report.MeasuredEnergyWh));
        Row(builder, "게이지 감소", Wh(report.GaugeDropWh));
        Row(builder, "센서/게이지", report.SensorToGaugeRatio is double ratio ? ratio.ToString("0.00", CultureInfo.InvariantCulture) : "비교 안 함");
        Row(builder, "환산 용량으로 본 사용 시간", Hours(report.MeasuredRuntimeHours));
        Row(builder, "게이지 완충 용량으로 본 사용 시간", Hours(report.GaugeRuntimeHours));
        builder.AppendLine("</table>");

        builder.AppendLine("<h2>해석</h2>");
        builder.AppendLine("<ul>");
        foreach (var line in Interpret(report))
            builder.AppendLine($"<li>{E(line)}</li>");
        builder.AppendLine("</ul>");

        builder.AppendLine("<h2>계산</h2>");
        builder.AppendLine("<div class=\"note\">");
        foreach (var line in report.Notes.Concat(context.ExtraNotes))
            builder.AppendLine($"<p>{E(line)}</p>");
        builder.AppendLine("</div>");

        builder.AppendLine("<h2>주의</h2>");
        if (report.Warnings.Count == 0)
        {
            builder.AppendLine("<p>없음</p>");
        }
        else
        {
            builder.AppendLine("<ul class=\"warn\">");
            foreach (var line in report.Warnings)
                builder.AppendLine($"<li>{E(line)}</li>");
            builder.AppendLine("</ul>");
        }

        builder.AppendLine("<footer>CheckBattery. 건강도는 배터리 컨트롤러의 완충 용량이고, 환산 용량은 이번 방전에서 잰 전력으로 늘려 잡은 값입니다.</footer>");
        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public const string CsvHeader =
        "timestamp,ac_online,charging,discharging,percent,voltage_v,power_w,remaining_wh,full_wh,design_wh";

    public static string Csv(IReadOnlyList<BatteryReading> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CsvHeader);
        foreach (var sample in samples)
            builder.AppendLine(CsvRow(sample));
        return builder.ToString();
    }

    public static string CsvRow(BatteryReading sample)
    {
        return string.Join(',',
            sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            sample.AcOnline ? "1" : "0",
            sample.Charging ? "1" : "0",
            sample.Discharging ? "1" : "0",
            Num(sample.ChargePercent),
            Num(sample.VoltageVolts),
            Num(sample.PowerWatts),
            Num(sample.RemainingCapacityWh),
            Num(sample.FullChargeCapacityWh),
            Num(sample.DesignCapacityWh));
    }

    public static string MonitorLine(BatteryReading reading)
    {
        var clock = reading.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{clock}  {PowerSource(reading)}  {Percent(reading.ChargePercent)}  {Volts(reading.VoltageVolts)}  {Power(reading)}  잔량 {Wh(reading.RemainingCapacityWh)}";
    }

    public static string Wh(double? value) =>
        value is double number ? number.ToString("0.00", CultureInfo.InvariantCulture) + " Wh" : "알 수 없음";

    public static string Percent(double? value) =>
        value is double number ? number.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "알 수 없음";

    public static string Volts(double? value) =>
        value is double number ? number.ToString("0.00", CultureInfo.InvariantCulture) + " V" : "알 수 없음";

    public static string Watts(double? value) =>
        value is double number ? number.ToString("0.00", CultureInfo.InvariantCulture) + " W" : "알 수 없음";

    public static string Power(BatteryReading reading)
    {
        if (reading.PowerWatts is not double watts)
            return "알 수 없음";
        if (watts < -0.05)
            return "방전 " + (-watts).ToString("0.00", CultureInfo.InvariantCulture) + " W";
        if (watts > 0.05)
            return "충전 " + watts.ToString("0.00", CultureInfo.InvariantCulture) + " W";
        return "0 W";
    }

    public static string PowerSource(BatteryReading reading)
    {
        if (reading.Charging || (reading.AcOnline && !reading.Discharging))
            return "전원 연결";
        if (reading.Discharging || !reading.AcOnline)
            return "배터리";
        return "알 수 없음";
    }

    public static string Runtime(BatteryReading reading)
    {
        if (reading.Discharging && reading.PowerWatts is < -0.3 && reading.RemainingCapacityWh is double remaining)
            return FormatHours(remaining / -reading.PowerWatts.Value);
        if (reading.EstimatedRuntimeMinutes is int minutes && minutes is > 0 and < 24 * 60)
            return FormatHours(minutes / 60.0);
        return "알 수 없음";
    }

    public static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        if (value.TotalSeconds < 60)
            return $"{Math.Max(0, (int)Math.Round(value.TotalSeconds))}초";

        var totalMinutes = (int)Math.Round(value.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0 && minutes == 0)
            return $"{hours}시간";
        if (hours > 0)
            return $"{hours}시간 {minutes}분";
        return $"{minutes}분";
    }

    public static string FormatHours(double hours) => FormatDuration(TimeSpan.FromHours(Math.Max(0, hours)));

    private static string Hours(double? hours) => hours is double value ? "약 " + FormatHours(value) : "알 수 없음";

    private static string When(DateTimeOffset timestamp) =>
        timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string Remaining(BatteryReading reading)
    {
        var amount = Wh(reading.RemainingCapacityWh);
        return reading.ChargePercent is double percent ? $"{amount} ({percent:0.0}%)" : amount;
    }

    private static string BatteryLabel(BatteryReading reading)
    {
        var name = string.IsNullOrWhiteSpace(reading.Name) ? "배터리" : reading.Name;
        return reading.BatteryCount > 1 ? $"{name} ({reading.BatteryCount}개)" : name;
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        builder.Append(label).Append(": ").AppendLine(value);
    }

    private static void Card(StringBuilder builder, string label, string value, string detail, string valueClass)
    {
        var css = string.IsNullOrEmpty(valueClass) ? "" : $" {valueClass}";
        builder.Append("<article class=\"card\"><div class=\"k\">").Append(E(label)).Append("</div>");
        builder.Append("<div class=\"v").Append(css).Append("\">").Append(E(value)).Append("</div>");
        builder.Append("<div class=\"d\">").Append(E(detail)).AppendLine("</div></article>");
    }

    private static void Row(StringBuilder builder, string label, string? value)
    {
        builder.Append("<tr><th>").Append(E(label)).Append("</th><td>");
        builder.Append(E(string.IsNullOrWhiteSpace(value) ? "알 수 없음" : value));
        builder.AppendLine("</td></tr>");
    }

    private static string CapacityMeters(DischargeReport report)
    {
        var design = report.DesignCapacityWh ?? 0;
        var full = report.GaugeFullChargeCapacityWh ?? 0;
        var estimated = report.EstimatedUsableCapacityWh ?? 0;
        var max = Math.Max(design, Math.Max(full, estimated));
        if (max <= 0)
            return "<p>비교할 용량 값이 없습니다.</p>";

        var builder = new StringBuilder();
        builder.AppendLine("<div class=\"meters\">");
        Meter(builder, "설계", design, max);
        Meter(builder, "완충", full, max);
        Meter(builder, "환산", estimated, max);
        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private static void Meter(StringBuilder builder, string label, double value, double max)
    {
        var width = max <= 0 || value <= 0 ? 0 : Math.Clamp(value / max * 100.0, 0, 100);
        builder.Append("<div class=\"meter\"><span class=\"ml\">").Append(E(label)).Append("</span>");
        builder.Append("<div class=\"track\"><div style=\"width:").Append(width.ToString("0.0", CultureInfo.InvariantCulture)).Append("%\"></div></div>");
        builder.Append("<span class=\"mv\">").Append(value <= 0 ? "없음" : E(value.ToString("0.00", CultureInfo.InvariantCulture) + " Wh")).AppendLine("</span></div>");
    }

    private static string GradeClass(string grade) => grade switch
    {
        "최상" or "양호" => "good",
        "주의" => "warn",
        "교체 권장" => "bad",
        _ => "",
    };

    private static string Num(double? value) =>
        value is double number ? number.ToString("0.###", CultureInfo.InvariantCulture) : "";

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    private const string Css =
        """
        :root { color-scheme: light; }
        body { margin: 0; background: #f3f5f7; color: #1d2733; font-family: "Malgun Gothic", "Segoe UI", sans-serif; line-height: 1.5; }
        main { max-width: 920px; margin: 28px auto; background: #fff; border: 1px solid #d5dde6; padding: 28px 32px 36px; }
        h1 { font-size: 22px; margin: 0; }
        h2 { font-size: 16px; margin: 28px 0 8px; }
        .sub { color: #5d6b7a; margin: 4px 0 16px; }
        .banner { background: #fff6df; border: 1px solid #ecd59a; padding: 8px 12px; }
        .cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
        .card { background: #f7f9fb; border: 1px solid #e3e9ef; padding: 10px 12px; }
        .k, .d { color: #5d6b7a; font-size: 12px; }
        .v { font-size: 22px; margin: 2px 0; }
        .good { color: #0d7a45; }
        .warn { color: #8a5a00; }
        .bad { color: #a12622; }
        table { width: 100%; border-collapse: collapse; }
        th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #e6ebf0; vertical-align: top; font-size: 14px; }
        th { width: 42%; color: #5d6b7a; font-weight: 600; }
        .note { background: #f7f9fb; padding: 4px 12px; }
        ul.warn { padding-left: 18px; }
        .meter { display: grid; grid-template-columns: 48px 1fr 110px; gap: 8px; align-items: center; margin: 6px 0; }
        .track { background: #e7edf2; height: 12px; }
        .track > div { background: #1f6f54; height: 12px; }
        .mv { font-variant-numeric: tabular-nums; }
        footer { color: #5d6b7a; font-size: 12px; margin-top: 28px; }
        @media (max-width: 720px) {
          main { margin: 0; border: 0; padding: 16px; }
          .cards { grid-template-columns: 1fr 1fr; }
          .meter { grid-template-columns: 48px 1fr; }
          .mv { grid-column: 2; }
        }
        """;
}
