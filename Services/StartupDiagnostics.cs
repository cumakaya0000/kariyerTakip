using System.Collections.Concurrent;
using KariyerTakip.Common;
using Microsoft.Extensions.Logging;

namespace KariyerTakip.Services;

public static class StartupDiagnostics
{
    private static readonly ConcurrentQueue<string> Messages = new();
    public static void Report(string message)
    {
        Messages.Enqueue(message);
        try
        {
            using var logger = new FileLoggerProvider();
            logger.CreateLogger("Startup").LogWarning("{Warning}", message);
        }
        catch (Exception) { /* Reporting must also work when the data directory is inaccessible. */ }
    }
    public static string[] Drain()
    {
        var messages = new List<string>();
        while (Messages.TryDequeue(out var message)) messages.Add(message);
        return messages.Distinct().ToArray();
    }
}
