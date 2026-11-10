namespace CheckBattery.Core;

public sealed record DischargeReport
{
    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset EndedAt { get; init; }

    public TimeSpan ObservedDuration { get; init; }

    public TimeSpan DischargeDuration { get; init; }

    public int SampleCount { get; init; }

    public int DischargeIntervals { get; init; }

    public double MeasuredEnergyWh { get; init; }

    public double? AverageDischargeWatts { get; init; }

    public double? StartPercent { get; init; }

    public double? EndPercent { get; init; }

    public double? GaugeDropWh { get; init; }

    /// <summary>측정 에너지를 잔량 비율로 나눠 100%까지 늘린 용량입니다.</summary>
    public double? EstimatedUsableCapacityWh { get; init; }

    public double? GaugeFullChargeCapacityWh { get; init; }

    public double? DesignCapacityWh { get; init; }

    public double? HealthPercent { get; init; }

    public string HealthGrade { get; init; } = "알 수 없음";

    /// <summary>컨트롤러가 보고한 완충 용량을 이번 평균 전력으로 나눈 시간입니다.</summary>
    public double? GaugeRuntimeHours { get; init; }

    /// <summary>환산한 실사용 용량을 이번 평균 전력으로 나눈 시간입니다.</summary>
    public double? MeasuredRuntimeHours { get; init; }

    /// <summary>센서 적산 에너지 / 같은 구간의 게이지 감소량입니다. 1에 가까울수록 둘이 맞습니다.</summary>
    public double? SensorToGaugeRatio { get; init; }

    public bool PowerEstimatedFromGauge { get; init; }

    public bool GaugeFilledGaps { get; init; }

    public int IgnoredGlitchIntervals { get; init; }

    public int ExcludedAcIntervals { get; init; }

    public int ExcludedGapIntervals { get; init; }

    public int? CycleCount { get; init; }

    public string? Chemistry { get; init; }

    public string? Manufacturer { get; init; }

    public string? SerialNumber { get; init; }

    public string? BatteryName { get; init; }

    public int BatteryCount { get; init; }

    public double? LastVoltageVolts { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record DischargeAnalyzeOptions
{
    /// <summary>전체 용량으로 환산하기 위해 필요한 최소 잔량 감소(%p)입니다.</summary>
    public double MinimumSocDropPoints { get; init; } = 5;

    /// <summary>이 값보다 짧은 방전은 환산값의 오차가 크다고 알립니다.</summary>
    public double ConfidentSocDropPoints { get; init; } = 15;

    /// <summary>노트북 방전으로 보기 어려운 전력은 센서 오류로 버립니다.</summary>
    public double MaxPlausibleWatts { get; init; } = 400;

    /// <summary>이보다 긴 간격은 절전으로 보고 적분에서 뺍니다.</summary>
    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromMinutes(5);
}
