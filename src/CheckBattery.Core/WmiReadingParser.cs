using System.Globalization;
using System.Text;

namespace CheckBattery.Core;

/// <summary>
/// root\wmi 와 Win32_Battery 속성을 용량·전력 단위로 바꿉니다.
/// WMI의 설계 용량과 완충 용량, 잔량, 전압, 충방전 속도는 밀리 단위입니다.
/// </summary>
public static class WmiReadingParser
{
    private const int UnknownRuntimeMinutes = 71582788;

    public static BatteryReading Parse(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> statuses,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> fullCapacities,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> staticData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> cycleCounts,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> win32Batteries,
        SystemPowerSnapshot? power,
        DateTimeOffset timestamp)
    {
        var remaining = SumMilliUnit(statuses, "RemainingCapacity");
        var full = SumMilliUnit(fullCapacities, "FullChargedCapacity");
        var design = SumMilliUnit(staticData, "DesignedCapacity");
        var voltage = AverageMilliUnit(statuses, "Voltage");
        var rates = ReadPower(statuses);
        var cycles = MaxInt(cycleCounts, "CycleCount");
        var chemistry = ChemistryFrom(win32Batteries);
        var name = FirstString(win32Batteries, "Name") ?? FirstString(win32Batteries, "DeviceID");
        var manufacturer = FirstString(staticData, "ManufactureName") ?? FirstString(win32Batteries, "Manufacturer");
        var serial = FirstString(staticData, "SerialNumber");
        var designMillivolts = FirstPositiveInt(win32Batteries, "DesignVoltage");
        var win32Percent = AveragePercent(win32Batteries, "EstimatedChargeRemaining");
        var win32Runtime = FirstPlausibleRuntime(win32Batteries, "EstimatedRunTime");
        var win32Status = FirstInt(win32Batteries, "BatteryStatus");

        var count = Math.Max(statuses.Count, Math.Max(win32Batteries.Count, staticData.Count));
        var wmiPresent = count > 0 || fullCapacities.Count > 0;
        var present = wmiPresent || power?.BatteryPresent == true;
        if (!wmiPresent && power?.BatteryPresent == false)
            present = false;

        double? percent = null;
        if (remaining is double remainingWh && full is double fullWh && fullWh > 0)
            percent = Math.Clamp(remainingWh / fullWh * 100.0, 0, 100);
        percent ??= win32Percent ?? power?.ChargePercent;

        var acFromWin32 = win32Status is 2 or 6 or 7 or 8 or 9;
        var chargingFromWin32 = win32Status is 6 or 7 or 8 or 9;
        var dischargingFromWin32 = win32Status == 1;

        var ac = AnyFlag(statuses, "PowerOnline") || acFromWin32 || power?.AcOnline == true;
        var charging = AnyFlag(statuses, "Charging") || chargingFromWin32 || power?.Charging == true;
        var discharging = AnyFlag(statuses, "Discharging") || dischargingFromWin32;

        double? watts = rates;
        if (watts is > 0.05)
        {
            charging = true;
            discharging = false;
        }
        else if (watts is < -0.05)
        {
            charging = false;
            discharging = true;
        }

        int? runtimeMinutes = null;
        if (discharging && watts is < -0.3 && remaining is double left)
            runtimeMinutes = (int)Math.Round(left / -watts.Value * 60.0);
        else if (win32Runtime is int minutes)
            runtimeMinutes = minutes;
        else if (power?.RuntimeSeconds is int seconds && seconds > 0 && seconds < 24 * 60 * 60)
            runtimeMinutes = seconds / 60;

        return new BatteryReading
        {
            Timestamp = timestamp,
            BatteryPresent = present,
            AcOnline = ac,
            Charging = charging,
            Discharging = discharging,
            ChargePercent = percent,
            VoltageVolts = voltage,
            PowerWatts = watts,
            RemainingCapacityWh = remaining,
            FullChargeCapacityWh = full,
            DesignCapacityWh = design,
            CycleCount = cycles,
            DesignVoltageMillivolts = designMillivolts,
            Chemistry = chemistry,
            Manufacturer = manufacturer,
            SerialNumber = serial,
            Name = name,
            EstimatedRuntimeMinutes = runtimeMinutes,
            BatteryCount = present ? Math.Max(count, 1) : 0,
        };
    }

    public static string? ChemistryName(object? raw)
    {
        if (raw is string text && !string.IsNullOrWhiteSpace(text))
            return MapChemistryText(text.Trim());

        if (AsInt(raw) is int code)
        {
            return code switch
            {
                1 => "기타",
                2 => "알 수 없음",
                3 => "납산",
                4 => "니켈 카드뮴",
                5 => "니켈 수소",
                6 => "리튬 이온",
                7 => "아연 공기",
                8 => "리튬 폴리머",
                _ => null,
            };
        }

        return null;
    }

    private static double? ReadPower(IReadOnlyList<IReadOnlyDictionary<string, object?>> statuses)
    {
        double dischargeMw = 0;
        double chargeMw = 0;
        var sawRate = false;
        var discharging = false;
        var charging = false;

        foreach (var row in statuses)
        {
            if (AsDouble(Value(row, "DischargeRate")) is double discharge && discharge > 0)
            {
                dischargeMw += discharge;
                sawRate = true;
            }

            if (AsDouble(Value(row, "ChargeRate")) is double charge && charge > 0)
            {
                chargeMw += charge;
                sawRate = true;
            }

            if (IsFlag(Value(row, "Discharging")))
                discharging = true;
            if (IsFlag(Value(row, "Charging")))
                charging = true;
        }

        if (!sawRate)
            return null;

        // 플래그와 반대편 속도가 어긋난 제조사가 있어, 플래그가 가리키는 쪽이 0이면 두 속도의 차로 되돌립니다.
        if (discharging && !charging && dischargeMw > 0)
            return -dischargeMw / 1000.0;
        if (charging && !discharging && chargeMw > 0)
            return chargeMw / 1000.0;
        return (chargeMw - dischargeMw) / 1000.0;
    }

    private static string? ChemistryFrom(IReadOnlyList<IReadOnlyDictionary<string, object?>> win32)
    {
        foreach (var row in win32)
        {
            var name = ChemistryName(Value(row, "Chemistry"));
            if (name is not null)
                return name;
        }

        return null;
    }

    private static string MapChemistryText(string text)
    {
        var key = text.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (key.Contains("LION", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("LiIon", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("LiIo", StringComparison.OrdinalIgnoreCase))
            return "리튬 이온";
        if (key.Contains("LIPO", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("LiP", StringComparison.OrdinalIgnoreCase))
            return "리튬 폴리머";
        if (key.Contains("NIMH", StringComparison.OrdinalIgnoreCase))
            return "니켈 수소";
        if (key.Contains("NICD", StringComparison.OrdinalIgnoreCase))
            return "니켈 카드뮴";
        return text;
    }

    private static double? SumMilliUnit(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        double sum = 0;
        var any = false;
        foreach (var row in rows)
        {
            if (AsDouble(Value(row, property)) is double value && value > 0)
            {
                sum += value;
                any = true;
            }
        }

        return any ? sum / 1000.0 : null;
    }

    private static double? AverageMilliUnit(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        double sum = 0;
        var count = 0;
        foreach (var row in rows)
        {
            if (AsDouble(Value(row, property)) is double value && value > 0)
            {
                sum += value;
                count++;
            }
        }

        return count == 0 ? null : sum / count / 1000.0;
    }

    private static double? AveragePercent(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        double sum = 0;
        var count = 0;
        foreach (var row in rows)
        {
            if (AsDouble(Value(row, property)) is double value && value is >= 0 and <= 100)
            {
                sum += value;
                count++;
            }
        }

        return count == 0 ? null : sum / count;
    }

    private static int? MaxInt(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        int? max = null;
        foreach (var row in rows)
        {
            if (AsInt(Value(row, property)) is int value && value >= 0)
                max = max is null ? value : Math.Max(max.Value, value);
        }

        return max;
    }

    private static int? FirstPositiveInt(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        foreach (var row in rows)
        {
            if (AsInt(Value(row, property)) is int value && value > 0)
                return value;
        }

        return null;
    }

    private static int? FirstInt(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        foreach (var row in rows)
        {
            if (AsInt(Value(row, property)) is int value)
                return value;
        }

        return null;
    }

    private static int? FirstPlausibleRuntime(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        foreach (var row in rows)
        {
            if (AsInt(Value(row, property)) is int minutes && minutes is > 0 and < 24 * 60 && minutes != UnknownRuntimeMinutes)
                return minutes;
        }

        return null;
    }

    private static string? FirstString(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        foreach (var row in rows)
        {
            var text = AsString(Value(row, property));
            if (text is not null)
                return text;
        }

        return null;
    }

    private static bool AnyFlag(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string property)
    {
        foreach (var row in rows)
        {
            if (IsFlag(Value(row, property)))
                return true;
        }

        return false;
    }

    private static object? Value(IReadOnlyDictionary<string, object?> row, string property)
    {
        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static bool IsFlag(object? raw)
    {
        if (raw is bool flag)
            return flag;
        return AsDouble(raw) is > 0;
    }

    private static double? AsDouble(object? raw)
    {
        switch (raw)
        {
            case null:
                return null;
            case bool flag:
                return flag ? 1 : 0;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    private static int? AsInt(object? raw)
    {
        if (AsDouble(raw) is not double value || value is < int.MinValue or > int.MaxValue)
            return null;
        return (int)Math.Round(value);
    }

    private static string? AsString(object? raw)
    {
        switch (raw)
        {
            case null:
                return null;
            case string text:
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            case byte[] bytes:
                var unicode = Encoding.Unicode.GetString(bytes).Trim('\0', ' ', '\uFEFF');
                if (!string.IsNullOrWhiteSpace(unicode) && unicode.Any(ch => !char.IsControl(ch)))
                    return unicode.Trim();
                var ascii = Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
                return string.IsNullOrWhiteSpace(ascii) ? null : ascii;
            default:
                return null;
        }
    }
}
