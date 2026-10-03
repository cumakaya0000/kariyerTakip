using System.Text.Json;
using System.Text.Json.Nodes;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SecretStore _secrets;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public ConfigurationStore(string? path = null, SecretStore? secrets = null)
    {
        _path = path ?? AppPaths.AppSettingsFile;
        _secrets = secrets ?? new SecretStore();
    }

    public async Task SaveAsync(AppConfig config)
    {
        await _writeLock.WaitAsync();
        try
        {
            var root = File.Exists(_path) ? JsonNode.Parse(await File.ReadAllTextAsync(_path))!.AsObject() : new JsonObject();
            var settings = JsonSerializer.SerializeToNode(config)!.AsObject();
            settings["Telegram"]!["BotToken"] = "";
            await _secrets.SaveAsync(config.Telegram.BotToken);
            root["KariyerTakip"] = settings;
            await AtomicFile.WriteAsync(_path, root.ToJsonString(JsonOptions));
        }
        finally { _writeLock.Release(); }
    }

    public static async Task BootstrapAsync(string? directory = null, string? exampleDirectory = null)
    {
        var baseDirectory = directory ?? AppPaths.BaseDirectory;
        var settingsPath = Path.Combine(baseDirectory, "appsettings.json");
        var profilePath = Path.Combine(baseDirectory, "profile.json");
        Directory.CreateDirectory(baseDirectory);
        // Validate before the configuration provider sees the files. Keep the original bytes in a backup.
        JsonRecovery.Read(settingsPath, () => new JsonObject { ["KariyerTakip"] = JsonSerializer.SerializeToNode(new AppConfig()) }, root =>
        {
            if (root["KariyerTakip"] == null) return true;
            var config = root["KariyerTakip"]!.Deserialize<AppConfig>();
            return config != null && config.Scan != null && config.Telegram != null && config.Desktop != null;
        });
        JsonRecovery.Read(profilePath, () => JsonSerializer.SerializeToNode(new ProfileOptions())!.AsObject(), root =>
        {
            if (root["Experience"] is JsonObject experience && experience["Years"] != null)
            {
                var years = experience["Years"]!.GetValue<double>();
                if (!double.IsFinite(years) || years < 0 || years > 100) return false;
            }
            if (root["OtherConditions"] != null && root["OtherConditions"] is not JsonObject) return false;
            if (root["OtherConditions"]?["MilitaryStatus"] != null) _ = root["OtherConditions"]!["MilitaryStatus"]!.GetValue<string>();
            return ProfileIsValid(root.Deserialize<ProfileOptions>());
        });
        foreach (var name in new[] { "appsettings", "profile" })
        {
            var destination = Path.Combine(baseDirectory, name + ".json");
            var example = Path.Combine(exampleDirectory ?? AppContext.BaseDirectory, name + ".example.json");
            if (!File.Exists(destination) && File.Exists(example))
                await AtomicFile.WriteAsync(destination, await File.ReadAllTextAsync(example));
        }
        // Import old plaintext credentials once, while preserving unrelated configuration.
        if (File.Exists(settingsPath))
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath));
            var token = root?["KariyerTakip"]?["Telegram"]?["BotToken"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(token))
            {
                await new SecretStore(Path.Combine(baseDirectory, "telegram.secret")).SaveAsync(token);
                root!["KariyerTakip"]!["Telegram"]!["BotToken"] = "";
                await AtomicFile.WriteAsync(settingsPath, root.ToJsonString(JsonOptions));
            }
        }
        if (File.Exists(profilePath))
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(profilePath))!.AsObject();
            if (root["Experience"] is JsonObject experience && experience["TotalMonths"] == null && experience["Years"] != null)
                experience["TotalMonths"] = (int)Math.Round(experience["Years"]!.GetValue<double>() * 12);
            if (root["MilitaryStatus"] == null && root["OtherConditions"]?["MilitaryStatus"] != null)
                root["MilitaryStatus"] = root["OtherConditions"]!["MilitaryStatus"]!.DeepClone();
            root["Experience"]?.AsObject().Remove("Years");
            root.Remove("OtherConditions"); // MaxAge is a posting condition, not a person's birth date.
            await AtomicFile.WriteAsync(profilePath, root.ToJsonString(JsonOptions));
        }
    }

    public static bool ProfileIsValid(ProfileOptions? profile) => profile != null && profile.Experience != null &&
        profile.KpssScores != null && profile.KpssScores.All(s => s != null) && profile.CityPreferences != null &&
        profile.DrivingLicenses != null && profile.Certificates != null && profile.WorkPreferences != null;
}
