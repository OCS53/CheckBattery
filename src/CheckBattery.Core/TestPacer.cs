namespace CheckBattery.Core;

public enum SampleDecision
{
    Continue,
    PauseOnAc,
    StopAtFloor,
    StopAtDuration,
}

public static class TestPacer
{
    public static SampleDecision Decide(
        BatteryReading sample,
        TimeSpan elapsedOnBattery,
        TimeSpan plannedDuration,
        double floorPercent)
    {
        if (sample.AcOnline || sample.Charging)
            return SampleDecision.PauseOnAc;

        if (BatteryMath.StateOfCharge(sample) is double percent && percent <= floorPercent)
            return SampleDecision.StopAtFloor;

        if (elapsedOnBattery >= plannedDuration)
            return SampleDecision.StopAtDuration;

        return SampleDecision.Continue;
    }
}
