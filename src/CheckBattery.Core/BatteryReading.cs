namespace CheckBattery.Core;

/// <summary>
/// 한 시점의 배터리 관찰값입니다. 전력은 방전이면 음수, 충전이면 양수입니다.
/// 용량 단위는 와트시(Wh)입니다.
/// </summary>
public sealed record BatteryReading
{
    public DateTimeOffset Timestamp { get; init; }

    public bool BatteryPresent { get; init; }

    public bool AcOnline { get; init; }

    public bool Charging { get; init; }

    public bool Discharging { get; init; }

    /// <summary>0에서 100 사이의 잔량 비율입니다.</summary>
    public double? ChargePercent { get; init; }

    public double? VoltageVolts { get; init; }

    public double? PowerWatts { get; init; }

    public double? RemainingCapacityWh { get; init; }

    public double? FullChargeCapacityWh { get; init; }

    public double? DesignCapacityWh { get; init; }

    public int? CycleCount { get; init; }

    public int? DesignVoltageMillivolts { get; init; }

    public string? Chemistry { get; init; }

    public string? Manufacturer { get; init; }

    public string? SerialNumber { get; init; }

    public string? Name { get; init; }

    /// <summary>운영체제가 직접 알려 준 남은 시간(분)입니다. 알 수 없으면 null입니다.</summary>
    public int? EstimatedRuntimeMinutes { get; init; }

    public int BatteryCount { get; init; }
}
