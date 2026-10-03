namespace KariyerTakip.Common;

public static class AppPaths
{
    private static string? _baseDirectory;

    public static string BaseDirectory
    {
        get
        {
            if (_baseDirectory != null) return _baseDirectory;

            // Check if portable mode is preferred (next to exe or current folder if config exists)
            var currentAppDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(currentAppDir, "appsettings.json")) ||
                File.Exists(Path.Combine(currentAppDir, "profile.json")))
            {
                _baseDirectory = currentAppDir;
                return _baseDirectory;
            }

            // Otherwise use LocalAppData
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _baseDirectory = Path.Combine(localAppData, "KariyerTakip");
            if (!Directory.Exists(_baseDirectory))
            {
                Directory.CreateDirectory(_baseDirectory);
            }

            return _baseDirectory;
        }
    }

    public static string AppSettingsFile => Path.Combine(BaseDirectory, "appsettings.json");
    public static string ProfileFile => Path.Combine(BaseDirectory, "profile.json");
    public static string DatabaseFile => Path.Combine(BaseDirectory, "kariyertakip.db");
    public static string LogsDirectory => Path.Combine(BaseDirectory, "logs");
}
