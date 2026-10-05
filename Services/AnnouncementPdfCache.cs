using System.Security.Cryptography;
using System.Text;
using KariyerTakip.Common;
using UglyToad.PdfPig;

namespace KariyerTakip.Services;

public sealed class AnnouncementPdfCache
{
    private readonly string _directory;
    public AnnouncementPdfCache(string? directory = null) =>
        _directory = Path.GetFullPath(directory ?? Path.Combine(AppPaths.BaseDirectory, "documents"));

    private string PathFor(string announcementGuid) => Path.Combine(_directory,
        "sbb-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(announcementGuid))).ToLowerInvariant() + ".pdf");

    public string? GetExistingPath(string announcementGuid)
    {
        var path = PathFor(announcementGuid);
        if (!File.Exists(path)) return null;
        try
        {
            using var document = PdfDocument.Open(path);
            return document.NumberOfPages > 0 ? path : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public async Task StoreAsync(string announcementGuid, byte[] bytes, CancellationToken token = default)
    {
        using (var document = PdfDocument.Open(bytes))
            if (document.NumberOfPages == 0) throw new FormatException("PDF belgesinde sayfa bulunamadı.");
        Directory.CreateDirectory(_directory);
        var path = PathFor(announcementGuid);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
