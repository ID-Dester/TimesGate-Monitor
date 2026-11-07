using System.Threading;

namespace TimesGateMonitor.App.Infrastructure;

internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex? _mutex;
    private bool _disposed;

    private SingleInstanceLease(Mutex mutex)
    {
        _mutex = mutex;
    }

    private SingleInstanceLease()
    {
    }

    public static bool TryAcquire(out SingleInstanceLease lease)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\TGm-PC-Monitor", out var createdNew);
        if (createdNew)
        {
            lease = new SingleInstanceLease(mutex);
            return true;
        }

        mutex.Dispose();
        lease = new SingleInstanceLease();
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
