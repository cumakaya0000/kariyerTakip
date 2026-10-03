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

        ConfigurationStore.BootstrapAsync().GetAwaiter().GetResult();
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
        var initialProfile = new ProfileOptions();
        builder.Configuration.Bind(initialProfile);
        var profileStore = new ProfileStore();
        var catalog = profileStore.Load(initialProfile);
        var activeProfile = catalog.Profiles.Single(p => p.Name == catalog.ActiveName).Profile;
        builder.Services.PostConfigure<ProfileOptions>(profile =>
        {
            foreach (var property in typeof(ProfileOptions).GetProperties().Where(p => p.CanWrite))
                property.SetValue(profile, property.GetValue(activeProfile));
        });
        builder.Services.PostConfigure<AppConfig>(config =>
        {
            if (string.IsNullOrWhiteSpace(config.Telegram.BotToken)) config.Telegram.BotToken = new SecretStore().Read();
        });

        // 3. Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddProvider(new GuiLoggerProvider());
        builder.Logging.AddProvider(new FileLoggerProvider());

        // 4. Register HTTP Clients & Core Services
        builder.Services.AddHttpClient<CareerGateClient>();
        builder.Services.AddHttpClient<TelegramNotifier>().RemoveAllLoggers(); // Telegram URLs contain the credential.
        builder.Services.AddSingleton<ConfigurationStore>();
        builder.Services.AddSingleton(profileStore);

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

        using var host = builder.Build();

        // 5. Execution mode
        if (isHeadless)
        {
            var logger = host.Services.GetRequiredService<ILogger<ScanCoordinator>>();
            logger.LogInformation("KariyerTakip --scan-once modunda başlatıldı (Görev Zamanlayıcı / Headless).");

            var coordinator = host.Services.GetRequiredService<ScanCoordinator>();
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (s, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancelHandler;
            try { return RunHeadlessAsync(coordinator, cancellation.Token).GetAwaiter().GetResult(); }
            finally { Console.CancelKeyPress -= cancelHandler; }
        }
        else
        {
            var mainForm = host.Services.GetRequiredService<MainForm>();
            Application.Run(mainForm);
            return 0;
        }
    }

    public static async Task<int> RunHeadlessAsync(ScanCoordinator coordinator, CancellationToken cancellationToken = default)
    {
        var result = await coordinator.RunScanAsync(cancellationToken);
        return GetExitCode(result.Status);
    }

    // Keep the WinForms entry thread STA; only the headless branch awaits asynchronous work.
    public static int GetExitCode(ScanStatus status) => status switch
    {
        ScanStatus.Success => 0, ScanStatus.Failed => 1, ScanStatus.Partial => 2,
        ScanStatus.Cancelled => 3, _ => 4
    };
}
