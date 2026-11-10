#if WINDOWS_WMI
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CheckBattery.Core;

namespace CheckBattery;

[SupportedOSPlatform("windows")]
internal sealed class WmiBatteryProbe : IBatteryProbe
{
    public BatteryReading Read()
    {
        return WmiReadingParser.Parse(
            Query(@"\\.\root\wmi", "BatteryStatus"),
            Query(@"\\.\root\wmi", "BatteryFullChargedCapacity"),
            Query(@"\\.\root\wmi", "BatteryStaticData"),
            Query(@"\\.\root\wmi", "BatteryCycleCount"),
            Query(@"\\.\root\cimv2", "Win32_Battery"),
            NativePower.Read(),
            DateTimeOffset.Now);
    }

    private static List<Dictionary<string, object?>> Query(string scope, string className)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, "SELECT * FROM " + className);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                        row[property.Name] = property.Value;
                    rows.Add(row);
                }
            }
        }
        catch (ManagementException)
        {
            // 데스크톱이거나 이 제조사에 해당 클래스가 없으면 다른 출처를 계속 봅니다.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (COMException)
        {
        }

        return rows;
    }
}
#endif
