namespace CheckBattery.Tests;

static class Approx
{
    public static void Equal(double expected, double? actual, double tolerance = 0.05)
    {
        Assert.NotNull(actual);
        var delta = Math.Abs(expected - actual.Value);
        Assert.True(delta <= tolerance, $"기대값 {expected} ± {tolerance}, 실제 {actual}");
    }
}
