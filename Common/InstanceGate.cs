namespace KariyerTakip.Common;

public sealed class InstanceGate : IDisposable
{
    public const string ApplicationMutexName = @"Global\KariyerTakip";
    private readonly Mutex _mutex;
    private bool _owned;
    public InstanceGate(string name = ApplicationMutexName) => _mutex = new Mutex(false, name);
    public bool TryAcquire()
    {
        if (_owned) return true;
        try { _owned = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _owned = true; }
        return _owned;
    }
    public void Dispose()
    {
        if (_owned) { _mutex.ReleaseMutex(); _owned = false; }
        _mutex.Dispose();
    }
}
