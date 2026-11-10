using System.Runtime.InteropServices;

namespace CheckBattery;

/// <summary>
/// 시험 중에 절전으로 들어가면 방전 곡선이 끊기므로, 시스템과 화면 유휴 타이머를 리셋합니다.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    private readonly bool _active;

    public KeepAwake()
    {
        if (!OperatingSystem.IsWindows())
            return;

        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        _active = true;
    }

    public void Dispose()
    {
        if (_active)
            SetThreadExecutionState(EsContinuous);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
