using System.Text.Json;
using KariyerTakip.Common;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public sealed class NamedProfile
{
    public string Name { get; set; } = "Ana Profil";
    public ProfileOptions Profile { get; set; } = new();
}

public sealed class ProfileCatalog
{
    public string ActiveName { get; set; } = "Ana Profil";
    public List<NamedProfile> Profiles { get; set; } = new();
}

public sealed class ProfileStore
{
    private readonly string _profilesPath;
    private readonly string _profilePath;
    public ProfileStore(string? profilesPath = null, string? profilePath = null)
    {
        _profilesPath = profilesPath ?? AppPaths.ProfilesFile;
        _profilePath = profilePath ?? AppPaths.ProfileFile;
    }
    public ProfileCatalog Load(ProfileOptions fallback)
    {
        var catalog = File.Exists(_profilesPath)
            ? JsonSerializer.Deserialize<ProfileCatalog>(File.ReadAllText(_profilesPath)) ?? new() : new ProfileCatalog();
        if (catalog.Profiles.Count == 0) catalog.Profiles.Add(new NamedProfile { Profile = fallback });
        if (!catalog.Profiles.Any(p => p.Name == catalog.ActiveName)) catalog.ActiveName = catalog.Profiles[0].Name;
        return catalog;
    }
    public async Task SaveAsync(ProfileCatalog catalog)
    {
        if (catalog.Profiles.Count == 0 || catalog.Profiles.Any(p => string.IsNullOrWhiteSpace(p.Name)) ||
            catalog.Profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Profiles.Count)
            throw new InvalidOperationException("Profil adları boş olmayan, benzersiz adlar olmalıdır.");
        var active = catalog.Profiles.Single(p => p.Name == catalog.ActiveName).Profile;
        var options = new JsonSerializerOptions { WriteIndented = true };
        // The catalog is the authoritative source; profile.json stays compatible with older versions.
        await AtomicFile.WriteAsync(_profilesPath, JsonSerializer.Serialize(catalog, options));
        await AtomicFile.WriteAsync(_profilePath, JsonSerializer.Serialize(active, options));
    }
}
