namespace CheckBattery.Core;

public static class BatteryMath
{
    public static double? StateOfCharge(BatteryReading reading)
    {
        if (reading.ChargePercent is double percent && percent is >= 0 and <= 100)
            return percent;

        if (reading.RemainingCapacityWh is double remaining &&
            reading.FullChargeCapacityWh is double full &&
            full > 0)
        {
            return Math.Clamp(remaining / full * 100.0, 0, 100);
        }

        return null;
    }

    public static double? HealthPercent(double? fullChargeWh, double? designWh)
    {
        if (fullChargeWh is not double full || designWh is not double design || design <= 0 || full < 0)
            return null;

        // 성적서에 보이는 소수 한 자리와 등급이 어긋나지 않도록 반올림합니다.
        return Math.Round(full / design * 100.0, 1, MidpointRounding.AwayFromZero);
    }

    public static string HealthGrade(double? healthPercent) => healthPercent switch
    {
        null => "알 수 없음",
        >= 90 => "최상",
        >= 80 => "양호",
        >= 70 => "보통",
        >= 60 => "주의",
        _ => "교체 권장",
    };

    public static string CycleComment(int? cycles) => cycles switch
    {
        null => "사이클 수를 읽지 못했습니다.",
        < 0 => "사이클 수를 읽지 못했습니다.",
        < 300 => "사이클이 적은 편입니다.",
        < 600 => "사이클이 보통 수준입니다.",
        < 1000 => "사이클이 많은 편입니다.",
        _ => "사이클이 매우 많습니다. 용량이 함께 줄었는지 건강도를 보세요.",
    };

    /// <summary>
    /// 방전 전력(W, 0 이상)입니다. 센서가 방전을 알려 주지 않으면 null입니다.
    /// </summary>
    public static double? DischargeWatts(BatteryReading reading)
    {
        if (reading.PowerWatts is not double power)
            return null;

        if (power < 0)
            return -power;

        if (reading.Discharging && power > 0)
            return power;

        return null;
    }
}
