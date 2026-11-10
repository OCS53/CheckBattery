using CheckBattery.Core;

namespace CheckBattery;

internal interface IBatteryProbe
{
    BatteryReading Read();
}

internal static class ProbeFactory
{
    public static IBatteryProbe Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "배터리 조회는 Windows 노트북에서만 동작합니다. 성적서 예시는 demo 명령으로 볼 수 있습니다.");
        }

#if WINDOWS_WMI
        return new WmiBatteryProbe();
#else
        throw new PlatformNotSupportedException(
            "이 빌드에는 Windows 배터리 조회가 들어 있지 않습니다. Windows에서 다시 빌드하세요.");
#endif
    }
}
