using Microsoft.Extensions.Logging;

namespace KariyerTakip.Services;

public class GuiLoggerProvider : ILoggerProvider
{
    public static event Action<string, LogLevel>? OnLogReceived;

    public ILogger CreateLogger(string categoryName)
    {
        return new GuiLogger(categoryName);
    }

    public void Dispose() { }

    private class GuiLogger : ILogger
    {
        private readonly string _categoryName;

        public GuiLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception != null)
            {
                message += $"\n{exception.Message}\n{exception.StackTrace}";
            }

            var shortCat = _categoryName.Contains('.') ? _categoryName.Substring(_categoryName.LastIndexOf('.') + 1) : _categoryName;
            var formatted = $"[{DateTime.Now:HH:mm:ss}] [{shortCat}] {message}";

            OnLogReceived?.Invoke(formatted, logLevel);
        }
    }
}
