using KariyerTakip.Models;

namespace KariyerTakip.Services;

public sealed class AnnouncementDocumentService(KamuIlanClient client, AnnouncementPdfCache cache)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> GetKamuPdfPathAsync(AnnouncementRecord record, CancellationToken token = default)
    {
        if (record.Source != AnnouncementSource.KamuIlan) throw new ArgumentException("Kamu İlan belgesi bekleniyor.", nameof(record));
        token.ThrowIfCancellationRequested();
        var cachedPath = cache.GetExistingPath(record.Guid);
        if (cachedPath != null) return cachedPath;
        await _gate.WaitAsync(token);
        try
        {
            cachedPath = cache.GetExistingPath(record.Guid);
            if (cachedPath != null) return cachedPath;
            // A saved encrypted URL belongs to the scanner's old session. Establish a fresh session and look it up again.
            var list = await client.GetActiveAnnouncementsAsync(token: token);
            if (!list.IsSuccess) throw new IOException(list.ErrorMessage ?? "Kamu İlan listesine erişilemedi.");
            var item = list.Data!.FirstOrDefault(i => i.Guid == record.Guid);
            if (item == null) throw new IOException("İlan güncel SBB listesinde bulunamadı ve kayıtlı PDF'si yok. Kamu İlan arşivinden kontrol edin.");
            var result = await client.GetAnnouncementDocumentAsync(item, token);
            // Image PDFs can be opened even when their text cannot be evaluated.
            cachedPath = cache.GetExistingPath(record.Guid);
            if (cachedPath != null) return cachedPath;
            throw new IOException(result.ErrorMessage ?? "Resmî PDF belgesi indirilemedi.");
        }
        finally { _gate.Release(); }
    }
}
