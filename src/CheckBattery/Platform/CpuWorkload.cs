using CheckBattery.Core;

namespace CheckBattery;

internal sealed class CpuWorkload : IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task[] _workers;
    private int _checksum;

    public CpuWorkload(LoadLevel level)
    {
        var threads = level switch
        {
            LoadLevel.Light => 1,
            LoadLevel.Steady => Math.Max(1, Environment.ProcessorCount / 2),
            _ => 0,
        };

        _workers = new Task[threads];
        for (var i = 0; i < threads; i++)
        {
            var light = level == LoadLevel.Light;
            _workers[i] = Task.Factory.StartNew(
                () => Work(light, _cancel.Token),
                _cancel.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 부하 스레드는 취소되면 끝나므로 집계 예외는 시험 결과와 관계가 없습니다.
        }

        _cancel.Dispose();
        GC.KeepAlive(Volatile.Read(ref _checksum));
    }

    private void Work(bool light, CancellationToken token)
    {
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        var value = Environment.CurrentManagedThreadId | 1;
        while (!token.IsCancellationRequested)
        {
            var start = Environment.TickCount64;
            do
            {
                for (var i = 1; i <= 8000; i++)
                    value = unchecked(value * 1664525 + i);
            }
            while (Environment.TickCount64 - start < 40 && !token.IsCancellationRequested);

            Volatile.Write(ref _checksum, value);
            if (light && token.WaitHandle.WaitOne(60))
                return;
        }
    }
}
