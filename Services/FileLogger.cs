using System.Text.RegularExpressions;
using KariyerTakip.Common;
using Microsoft.Extensions.Logging;

namespace KariyerTakip.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _lock = new();
    public FileLoggerProvider(string? directory = null)
    {
        _directory = directory ?? AppPaths.LogsDirectory;
        Directory.CreateDirectory(_directory);
    }
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    public static string Redact(string text) => Regex.Replace(text, @"bot\d+:[A-Za-z0-9_-]+", "bot[REDACTED]");
    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var message = Redact($"{DateTime.UtcNow:O} [{level}] [{category}] {formatter(state, exception)} {exception}");
            try
            {
                lock (owner._lock) File.AppendAllText(Path.Combine(owner._directory, $"kariyertakip-{DateTime.UtcNow:yyyy-MM-dd}.log"), message + Environment.NewLine);
            }
            catch (IOException) { /* Logging failure must not stop a scan. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
