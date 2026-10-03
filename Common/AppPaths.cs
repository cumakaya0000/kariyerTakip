namespace KariyerTakip.Common;

public static class AppPaths
{
    private static string? _baseDirectory;

    public static string BaseDirectory
    {
        get
        {
            if (_baseDirectory != null) return _baseDirectory;

            _baseDirectory = ResolveBaseDirectory(AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetEnvironmentVariable("KARIYERTAKIP_DATA_DIR"));
            return _baseDirectory;
        }
    }

    public static string ResolveBaseDirectory(string appDirectory, string localAppData, string? overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory)) return Path.GetFullPath(overrideDirectory);
        if (File.Exists(Path.Combine(appDirectory, "portable.flag")) ||
            File.Exists(Path.Combine(appDirectory, "appsettings.json")) ||
            File.Exists(Path.Combine(appDirectory, "profile.json"))) return Path.GetFullPath(appDirectory);
        return Path.Combine(localAppData, "KariyerTakip");
    }

    public static string AppSettingsFile => Path.Combine(BaseDirectory, "appsettings.json");
    public static string ProfileFile => Path.Combine(BaseDirectory, "profile.json");
    public static string DatabaseFile => Path.Combine(BaseDirectory, "kariyertakip.db");
    public static string LogsDirectory => Path.Combine(BaseDirectory, "logs");
    public static string ProfilesFile => Path.Combine(BaseDirectory, "profiles.json");
    public static string TelegramSecretFile => Path.Combine(BaseDirectory, "telegram.secret");
}
