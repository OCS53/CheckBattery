namespace CheckBattery.Core;

public static class DischargeAnalyzer
{
    public static DischargeReport Analyze(
        IReadOnlyList<BatteryReading> samples,
        DischargeAnalyzeOptions? options = null)
    {
        options ??= new DischargeAnalyzeOptions();
        var warnings = new List<string>();
        var notes = new List<string>();

        if (samples.Count == 0)
        {
            warnings.Add("기록된 샘플이 없습니다.");
            return new DischargeReport { Warnings = warnings, Notes = notes };
        }

        var first = samples[0];
        var last = samples[^1];
        var identity = LatestIdentity(samples);

        double sensorWh = 0;
        double filledWh = 0;
        double sensorSeconds = 0;
        double filledSeconds = 0;
        var sensorIntervals = 0;
        var filledIntervals = 0;
        double agreementSensorWh = 0;
        double agreementGaugeWh = 0;
        var glitches = 0;
        var acIntervals = 0;
        var gapIntervals = 0;
        var rebound = false;
        BatteryReading? windowStart = null;
        BatteryReading? windowEnd = null;

        for (var i = 1; i < samples.Count; i++)
        {
            var earlier = samples[i - 1];
            var later = samples[i];
            var dt = later.Timestamp - earlier.Timestamp;
            if (dt <= TimeSpan.Zero)
                continue;

            if (dt > options.MaxInterval)
            {
                gapIntervals++;
                continue;
            }

            if (earlier.AcOnline || later.AcOnline || earlier.Charging || later.Charging)
            {
                acIntervals++;
                continue;
            }

            var earlierWatts = BatteryMath.DischargeWatts(earlier);
            var laterWatts = BatteryMath.DischargeWatts(later);
            if (earlierWatts > options.MaxPlausibleWatts || laterWatts > options.MaxPlausibleWatts)
            {
                glitches++;
                continue;
            }

            var accepted = false;
            if (earlierWatts is double left && laterWatts is double right && (left > 0 || right > 0))
            {
                var slice = (left + right) / 2.0 * dt.TotalHours;
                sensorWh += slice;
                sensorSeconds += dt.TotalSeconds;
                sensorIntervals++;
                agreementSensorWh += slice;
                if (earlier.RemainingCapacityWh is double from && later.RemainingCapacityWh is double to)
                    agreementGaugeWh += from - to;
                accepted = true;
            }
            else if (earlier.RemainingCapacityWh is double from &&
                     later.RemainingCapacityWh is double to &&
                     from > to)
            {
                filledWh += from - to;
                filledSeconds += dt.TotalSeconds;
                filledIntervals++;
                accepted = true;
            }
            else if (earlier.RemainingCapacityWh is double fromWh &&
                     later.RemainingCapacityWh is double toWh &&
                     toWh > fromWh + 0.2)
            {
                rebound = true;
            }

            if (!accepted)
                continue;

            windowStart ??= earlier;
            windowEnd = later;
        }

        var fromGaugeOnly = sensorWh < 0.05 && filledWh > 0;
        var measuredWh = fromGaugeOnly ? filledWh : sensorWh + filledWh;
        var seconds = fromGaugeOnly ? filledSeconds : sensorSeconds + filledSeconds;
        double? averageWatts = seconds >= 1
            ? measuredWh / (seconds / 3600.0)
            : null;

        double? startPercent = windowStart is null ? null : BatteryMath.StateOfCharge(windowStart);
        double? endPercent = windowEnd is null ? null : BatteryMath.StateOfCharge(windowEnd);
        double? socDrop = startPercent is double start && endPercent is double end && end < start
            ? start - end
            : null;

        double? gaugeDrop = null;
        if (windowStart?.RemainingCapacityWh is double remainStart &&
            windowEnd?.RemainingCapacityWh is double remainEnd &&
            remainStart >= remainEnd)
        {
            gaugeDrop = remainStart - remainEnd;
        }

        double? estimated = null;
        if (socDrop is double drop && drop >= options.MinimumSocDropPoints && measuredWh > 0)
            estimated = measuredWh / drop * 100.0;

        double? ratio = null;
        if (!fromGaugeOnly && agreementGaugeWh > 0.05 && agreementSensorWh > 0)
            ratio = agreementSensorWh / agreementGaugeWh;

        var runtimeOk = averageWatts is >= 1;
        double? gaugeRuntime = runtimeOk && identity.FullChargeWh is double fullForRuntime
            ? fullForRuntime / averageWatts
            : null;
        double? measuredRuntime = runtimeOk && estimated is double estimatedForRuntime
            ? estimatedForRuntime / averageWatts
            : null;

        if (samples.Count < 2)
            warnings.Add("샘플이 하나뿐이라 방전 에너지를 적분하지 못했습니다.");
        if (windowStart is null)
            warnings.Add("배터리 방전 구간을 찾지 못했습니다. 어댑터를 뺀 상태에서 시험하세요.");
        if (acIntervals > 0)
            warnings.Add("어댑터가 연결된 구간은 방전량에서 뺐습니다.");
        if (gapIntervals > 0)
            warnings.Add("절전으로 보이는 긴 간격은 방전량에서 뺐습니다.");
        if (glitches > 0)
            warnings.Add("비정상적으로 큰 전력 값은 센서 오류로 보고 버렸습니다.");
        if (rebound)
            warnings.Add("방전 중인데 잔량이 늘어난 구간이 있습니다. 게이지가 보정 중일 수 있습니다.");
        if (fromGaugeOnly)
        {
            warnings.Add("전력 센서를 읽지 못해 잔량 감소만으로 에너지를 계산했습니다. 컨트롤러 게이지와 독립된 확인은 아닙니다.");
        }
        else if (filledWh > 0.05)
        {
            warnings.Add("전력 값이 비어 있던 구간은 잔량 감소로 보충했습니다.");
        }

        if (socDrop is null || socDrop < options.MinimumSocDropPoints)
        {
            warnings.Add($"잔량 변화가 {options.MinimumSocDropPoints:0}퍼센트포인트보다 작아 전체 용량으로 환산하지 않았습니다.");
        }
        else if (socDrop < options.ConfidentSocDropPoints)
        {
            warnings.Add("방전 구간이 짧습니다. 환산 용량은 이 구간의 전력이 끝까지 유지된다고 본 값이라 오차가 클 수 있습니다.");
        }

        if (identity.Health is < 80)
            warnings.Add("완충 용량이 설계 용량의 80% 미만입니다.");

        if (ratio is < 0.85 or > 1.15)
            warnings.Add("전력 센서로 적산한 에너지와 잔량 게이지의 감소량이 15% 넘게 다릅니다.");

        if (estimated is double estimatedWh && identity.FullChargeWh is double gaugeFull && gaugeFull > 0)
        {
            var relative = Math.Abs(estimatedWh - gaugeFull) / gaugeFull;
            if (relative > 0.25)
                warnings.Add("환산 용량과 컨트롤러가 보고한 완충 용량 차이가 큽니다. 시험을 더 길게 하면 오차가 줄어듭니다.");
        }

        if (estimated is double estimatedCapacity && identity.DesignWh is double design && design > 0 &&
            (estimatedCapacity > design * 2 || estimatedCapacity < design * 0.3))
        {
            warnings.Add("환산 용량이 설계 용량과 많이 다릅니다. 전력 센서 단위를 의심하세요.");
        }

        if (identity.DesignWh is < 5 or > 200)
            warnings.Add("읽은 용량이 노트북 배터리로 보기 어렵습니다. 펌웨어가 다른 단위로 보고했을 수 있습니다.");

        if (averageWatts is < 1 && measuredWh > 0)
            warnings.Add("평균 전력이 1W 미만이라 사용 시간으로 환산하지 않았습니다.");

        notes.Add("에너지는 방전 중 전력(W)을 시간으로 적분한 값입니다. 전력이 없으면 그 구간의 잔량 감소(Wh)를 더합니다.");
        notes.Add("환산 용량 = 사용 에너지 ÷ (시작 잔량% − 종료 잔량%) × 100.");
        notes.Add("건강도 = 컨트롤러가 보고한 완충 용량 ÷ 설계 용량 × 100.");

        return new DischargeReport
        {
            StartedAt = first.Timestamp,
            EndedAt = last.Timestamp,
            ObservedDuration = last.Timestamp - first.Timestamp,
            DischargeDuration = TimeSpan.FromSeconds(seconds),
            SampleCount = samples.Count,
            DischargeIntervals = fromGaugeOnly ? filledIntervals : sensorIntervals + filledIntervals,
            MeasuredEnergyWh = measuredWh,
            AverageDischargeWatts = averageWatts,
            StartPercent = startPercent,
            EndPercent = endPercent,
            GaugeDropWh = gaugeDrop,
            EstimatedUsableCapacityWh = estimated,
            GaugeFullChargeCapacityWh = identity.FullChargeWh,
            DesignCapacityWh = identity.DesignWh,
            HealthPercent = identity.Health,
            HealthGrade = BatteryMath.HealthGrade(identity.Health),
            GaugeRuntimeHours = gaugeRuntime,
            MeasuredRuntimeHours = measuredRuntime,
            SensorToGaugeRatio = ratio,
            PowerEstimatedFromGauge = fromGaugeOnly,
            GaugeFilledGaps = !fromGaugeOnly && filledWh > 0.05,
            IgnoredGlitchIntervals = glitches,
            ExcludedAcIntervals = acIntervals,
            ExcludedGapIntervals = gapIntervals,
            CycleCount = identity.Cycles,
            Chemistry = identity.Chemistry,
            Manufacturer = identity.Manufacturer,
            SerialNumber = identity.Serial,
            BatteryName = identity.Name,
            BatteryCount = identity.Count,
            LastVoltageVolts = identity.Voltage,
            Warnings = warnings,
            Notes = notes,
        };
    }

    private readonly record struct Identity(
        double? FullChargeWh,
        double? DesignWh,
        double? Health,
        int? Cycles,
        string? Chemistry,
        string? Manufacturer,
        string? Serial,
        string? Name,
        int Count,
        double? Voltage);

    private static Identity LatestIdentity(IReadOnlyList<BatteryReading> samples)
    {
        double? full = null;
        double? design = null;
        int? cycles = null;
        string? chemistry = null;
        string? manufacturer = null;
        string? serial = null;
        string? name = null;
        var count = 0;
        double? voltage = null;

        foreach (var sample in samples)
        {
            if (sample.FullChargeCapacityWh is double fullWh)
                full = fullWh;
            if (sample.DesignCapacityWh is double designWh)
                design = designWh;
            if (sample.CycleCount is int cycle)
                cycles = cycle;
            if (!string.IsNullOrWhiteSpace(sample.Chemistry))
                chemistry = sample.Chemistry;
            if (!string.IsNullOrWhiteSpace(sample.Manufacturer))
                manufacturer = sample.Manufacturer;
            if (!string.IsNullOrWhiteSpace(sample.SerialNumber))
                serial = sample.SerialNumber;
            if (!string.IsNullOrWhiteSpace(sample.Name))
                name = sample.Name;
            if (sample.BatteryCount > count)
                count = sample.BatteryCount;
            if (sample.VoltageVolts is double volts)
                voltage = volts;
        }

        return new Identity(
            full,
            design,
            BatteryMath.HealthPercent(full, design),
            cycles,
            chemistry,
            manufacturer,
            serial,
            name,
            count,
            voltage);
    }
}
