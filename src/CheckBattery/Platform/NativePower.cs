using System.Runtime.InteropServices;
using CheckBattery.Core;

namespace CheckBattery;

internal static class NativePower
{
    public static SystemPowerSnapshot? Read()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var status = new SystemPowerStatusNative();
        if (!GetSystemPowerStatus(ref status))
            return null;

        bool? present = status.BatteryFlag == 255 ? null : status.BatteryFlag != 128;
        bool? ac = status.AcLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
        double? percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        bool? charging = status.BatteryFlag == 255 ? null : (status.BatteryFlag & 8) != 0;
        int? runtime = status.BatteryLifeTime is > 0 and < 24 * 60 * 60 ? status.BatteryLifeTime : null;

        return new SystemPowerSnapshot
        {
            AcOnline = ac,
            BatteryPresent = present,
            ChargePercent = percent,
            Charging = charging,
            RuntimeSeconds = runtime,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatusNative
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(ref SystemPowerStatusNative status);
}
