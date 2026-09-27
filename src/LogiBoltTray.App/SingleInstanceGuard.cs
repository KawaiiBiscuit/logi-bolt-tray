using System.Threading;

namespace LogiBoltTray.App;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose() => _mutex.Dispose();
}
