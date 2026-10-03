using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using KariyerTakip.Common;

namespace KariyerTakip.Services;

// Windows DPAPI binds the encrypted token to the current Windows account.
public sealed class SecretStore
{
    private readonly string _path;
    public SecretStore(string? path = null) => _path = path ?? AppPaths.TelegramSecretFile;
    public string Read()
    {
        try
        {
            var content = File.Exists(_path) ? File.ReadAllText(_path) : "";
            return string.IsNullOrWhiteSpace(content) ? "" : Transform(Convert.FromBase64String(content), false);
        }
        catch (Exception ex) when (ex is Win32Exception or FormatException or IOException or UnauthorizedAccessException)
        {
            StartupDiagnostics.Report("Telegram token okunamadı, yeniden girin. Görevi tokenı kaydeden Windows hesabıyla çalıştırın.");
            return "";
        }
    }
    public Task SaveAsync(string token) => AtomicFile.WriteAsync(_path,
        string.IsNullOrEmpty(token) ? "" : Transform(Encoding.UTF8.GetBytes(token), true));

    private static string Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Telegram anahtarı Windows hesabıyla korunamadı/okunamadı.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return encrypt ? Convert.ToBase64String(result) : Encoding.UTF8.GetString(result);
        }
        finally
        {
            for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            Array.Clear(bytes);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr handle);
}
