using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KariyerTakip.Common;
using KariyerTakip.Forms;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;

namespace KariyerTakip;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var isHeadless = args.Any(a => a.Equals("--scan-once", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("--headless", StringComparison.OrdinalIgnoreCase));

        if (!isHeadless)
        {
            ApplicationConfiguration.Initialize();
        }

        var builder = Host.CreateApplicationBuilder(args);

        // 1. Configuration files from deterministic AppPaths
        builder.Configuration
            .SetBasePath(AppPaths.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("profile.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "KARIYERTAKIP_");

        // 2. Options bindings
        builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("KariyerTakip"));
        builder.Services.Configure<ProfileOptions>(builder.Configuration);

        // 3. Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddProvider(new GuiLoggerProvider());

        // 4. Register HTTP Clients & Core Services
        builder.Services.AddHttpClient<CareerGateClient>();
        builder.Services.AddHttpClient<TelegramNotifier>();

        builder.Services.AddSingleton<DocumentReader>();
        builder.Services.AddSingleton<RequirementExtractor>();
        builder.Services.AddSingleton<EligibilityEvaluator>();
        builder.Services.AddSingleton<ChangeDetector>();
        builder.Services.AddSingleton<IAnnouncementRepository, AnnouncementRepository>();
        builder.Services.AddSingleton<NotificationDispatcher>();
        builder.Services.AddSingleton<ScanCoordinator>();

        if (!isHeadless)
        {
            builder.Services.AddSingleton<MainForm>();
        }

        var host = builder.Build();

        // 5. Execution mode
        if (isHeadless)
        {
            var logger = host.Services.GetRequiredService<ILogger<ScanCoordinator>>();
            logger.LogInformation("KariyerTakip --scan-once modunda başlatıldı (Görev Zamanlayıcı / Headless).");

            var coordinator = host.Services.GetRequiredService<ScanCoordinator>();
            var result = coordinator.RunScanAsync().GetAwaiter().GetResult();

            return result.Status == ScanStatus.Success ? 0 : 1;
        }
        else
        {
            var mainForm = host.Services.GetRequiredService<MainForm>();
            Application.Run(mainForm);
            return 0;
        }
    }
}
