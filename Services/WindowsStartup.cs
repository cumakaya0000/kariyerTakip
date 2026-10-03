using Microsoft.Win32;

namespace KariyerTakip.Services;

public static class WindowsStartup
{
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("KariyerTakip") is string command && !string.IsNullOrWhiteSpace(command);
    }
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        ApplyTo(key, enabled, Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı."), AppContext.BaseDirectory);
    }
    public static void ApplyTo(RegistryKey key, bool enabled, string executable, string applicationDirectory)
    {
        if (!enabled) { key.DeleteValue("KariyerTakip", throwOnMissingValue: false); return; }
        var command = $"\"{executable}\"";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            command += $" \"{Path.Combine(applicationDirectory, "KariyerTakip.dll")}\"";
        key.SetValue("KariyerTakip", command);
    }
}
