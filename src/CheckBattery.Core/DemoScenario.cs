namespace CheckBattery.Core;

/// <summary>
/// 성적서와 계산 검증에 쓰는 가상 방전입니다. 잔량은 전력의 사다리꼴 적분과 같게 줄어듭니다.
/// </summary>
public static class DemoScenario
{
    public const double DesignWh = 72.0;
    public const double HealthyFullWh = 64.8;
    public const double WornFullWh = 46.8;
    public const double BaseWatts = 22.0;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    public const int Steps = 80;

    public static IReadOnlyList<BatteryReading> Create(bool worn, DateTimeOffset? start = null)
    {
        var full = worn ? WornFullWh : HealthyFullWh;
        var origin = start ?? new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(9));
        var watts = new double[Steps + 1];
        for (var i = 0; i <= Steps; i++)
            watts[i] = BaseWatts + ((i % 5) - 2) * 0.2;

        var samples = new List<BatteryReading>(Steps + 1);
        var remaining = full * 0.80;
        for (var i = 0; i <= Steps; i++)
        {
            samples.Add(new BatteryReading
            {
                Timestamp = origin + Step * i,
                BatteryPresent = true,
                AcOnline = false,
                Charging = false,
                Discharging = true,
                ChargePercent = remaining / full * 100.0,
                VoltageVolts = 11.70 - i * 0.004,
                PowerWatts = -watts[i],
                RemainingCapacityWh = remaining,
                FullChargeCapacityWh = full,
                DesignCapacityWh = DesignWh,
                CycleCount = worn ? 860 : 180,
                DesignVoltageMillivolts = 11400,
                Chemistry = "리튬 이온",
                Manufacturer = "데모셀",
                SerialNumber = worn ? "DEMO-WORN" : "DEMO-HEALTHY",
                Name = "내장 배터리",
                BatteryCount = 1,
            });

            if (i < Steps)
            {
                var average = (watts[i] + watts[i + 1]) / 2.0;
                remaining -= average * Step.TotalHours;
            }
        }

        return samples;
    }
}
