namespace CheckBattery.Core;

public enum LoadLevel
{
    None = 0,
    Light = 1,
    Steady = 2,
}

public static class LoadLevelText
{
    public static bool TryParse(string text, out LoadLevel level)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "none":
            case "off":
            case "없음":
                level = LoadLevel.None;
                return true;
            case "light":
            case "가벼움":
                level = LoadLevel.Light;
                return true;
            case "steady":
            case "기준":
                level = LoadLevel.Steady;
                return true;
            default:
                level = LoadLevel.None;
                return false;
        }
    }

    public static string Name(LoadLevel level) => level switch
    {
        LoadLevel.None => "없음",
        LoadLevel.Light => "가벼움",
        LoadLevel.Steady => "기준",
        _ => level.ToString(),
    };

    public static string Describe(LoadLevel level) => level switch
    {
        LoadLevel.None => "추가 부하 없음. 시험 동안 노트북을 쓰는 그대로의 전력입니다.",
        LoadLevel.Light => "가벼운 CPU 부하입니다. 스레드 1개가 약 40%만 일해 절전으로 전력이 출렁이는 것을 줄이고, 발열은 낮게 유지합니다.",
        LoadLevel.Steady => "고정 CPU 부하입니다. 전체 코어의 절반을 사용해 같은 조건으로 다시 잴 수 있습니다. 일상 사용보다 배터리가 빨리 닳고 따뜻해질 수 있습니다.",
        _ => "",
    };
}
