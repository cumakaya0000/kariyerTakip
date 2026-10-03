using KariyerTakip.Models;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public sealed class ScanPresenter(ScanCoordinator coordinator) : IDisposable
{
    private CancellationTokenSource? _cancellation;
    public bool IsRunning => _cancellation != null;
    public async Task<ScanRunResult> RunAsync(ProfileOptions profile)
    {
        if (IsRunning) return new ScanRunResult { Status = ScanStatus.Running };
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try { return await Task.Run(() => coordinator.RunScanAsync(cancellation.Token, profile)); }
        finally { _cancellation = null; }
    }
    public void Cancel() => _cancellation?.Cancel();
    public void Dispose() => Cancel();
}
