using Microsoft.Win32;

namespace KariyerTakip.Services;

public static class WindowsStartup
{
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (!enabled) { key.DeleteValue("KariyerTakip", throwOnMissingValue: false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        var command = $"\"{executable}\"";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            command += $" \"{Path.Combine(AppContext.BaseDirectory, "KariyerTakip.dll")}\"";
        key.SetValue("KariyerTakip", command);
    }
}
