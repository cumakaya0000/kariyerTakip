using System.Text.Json;
using KariyerTakip.Services;

namespace KariyerTakip.Common;

public static class JsonRecovery
{
    public static T Read<T>(string path, Func<T> defaults, Func<T, bool>? validate = null)
    {
        if (!File.Exists(path)) return defaults();
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new JsonException("JSON boş.");
            if (validate != null && !validate(value)) throw new JsonException("Beklenen yapı bulunamadı.");
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            var backup = path + ".bak";
            if (File.Exists(backup)) backup += "." + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
            var value = defaults();
            AtomicFile.WriteAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })).GetAwaiter().GetResult();
            StartupDiagnostics.Report($"{Path.GetFileName(path)} bozuk olduğu için {Path.GetFileName(backup)} olarak yedeklendi. Varsayılan ayarlarla devam ediliyor.");
            return value;
        }
    }
}
