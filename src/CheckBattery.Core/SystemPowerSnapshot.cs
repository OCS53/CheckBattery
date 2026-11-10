namespace CheckBattery.Core;

/// <summary>
/// GetSystemPowerStatus가 주는 요약입니다. WMI에 값이 없을 때의 보충 자료입니다.
/// </summary>
public sealed record SystemPowerSnapshot
{
    public bool? AcOnline { get; init; }

    public bool? BatteryPresent { get; init; }

    public double? ChargePercent { get; init; }

    public bool? Charging { get; init; }

    /// <summary>남은 시간(초)입니다. 알 수 없으면 null입니다.</summary>
    public int? RuntimeSeconds { get; init; }
}
