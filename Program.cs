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
        var headless = args.Any(a => a.Equals("--scan-once", StringComparison.OrdinalIgnoreCase) || a.Equals("--headless", StringComparison.OrdinalIgnoreCase));
        if (!headless)
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception, false);
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception, headless);
        try
        {
            using var instance = new InstanceGate();
            if (!instance.TryAcquire())
            {
                StartupDiagnostics.Report("KariyerTakip zaten açık. Zamanlanmış tarama sonraki çalışmaya bırakıldı.");
                if (!headless) MessageBox.Show("KariyerTakip zaten açık. Açık pencereyi veya sistem tepsisini kontrol edin.", "KariyerTakip");
                return 4;
            }
            return Run(args, headless);
        }
        catch (UnauthorizedAccessException)
        {
            StartupDiagnostics.Report("Başka bir Windows hesabındaki KariyerTakip oturumu veya veri erişim izni işlemi engelledi.");
            if (!headless) MessageBox.Show("KariyerTakip oturumu veya veri klasörü erişilemiyor. Çalışan oturumu ve klasör izinlerini kontrol edin.", "KariyerTakip");
            return 4;
        }
        catch (Exception ex) { ReportFatal(ex, headless); return 1; }
    }

    private static void ReportFatal(Exception? exception, bool headless)
    {
        StartupDiagnostics.Report($"Uygulama hatası: {exception?.GetType().Name}. Günlükleri kontrol edin.");
        if (!headless) MessageBox.Show("KariyerTakip bir hata nedeniyle işlemi tamamlayamadı. Veri klasöründeki logs günlüklerini kontrol edin.", "KariyerTakip", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static int Run(string[] args, bool isHeadless)
    {

        ConfigurationStore.BootstrapAsync().GetAwaiter().GetResult();
        var builder = Host.CreateApplicationBuilder(args);

        // 1. Configuration files from deterministic AppPaths
        builder.Configuration
            .SetBasePath(AppPaths.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("profile.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "KARIYERTAKIP_");

        // 2. Options bindings
        builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("KariyerTakip"));
        builder.Services.Configure<ProfileOptions>(builder.Configuration);
        var initialProfile = new ProfileOptions();
        builder.Configuration.Bind(initialProfile);
        var profileStore = new ProfileStore();
        var catalog = profileStore.Load(initialProfile);
        var activeProfile = catalog.Profiles.First(p => p.Name == catalog.ActiveName).Profile;
        builder.Services.PostConfigure<ProfileOptions>(profile =>
        {
            foreach (var property in typeof(ProfileOptions).GetProperties().Where(p => p.CanWrite))
                property.SetValue(profile, property.GetValue(activeProfile));
        });
        builder.Services.PostConfigure<AppConfig>(config =>
        {
            if (string.IsNullOrWhiteSpace(config.Telegram.BotToken)) config.Telegram.BotToken = new SecretStore().Read();
            config.Desktop.StartWithWindows = WindowsStartup.IsEnabled();
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
        builder.Services.AddSingleton<DeadlineReminderService>();
        builder.Services.AddSingleton<ApiHealthMonitor>();
        builder.Services.AddSingleton<ScanCoordinator>();

        if (!isHeadless)
        {
            builder.Services.AddSingleton<MainForm>();
        }

        using var host = builder.Build();
        _ = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppConfig>>().Value;

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
            mainForm.Shown += (_, _) =>
            {
                var warnings = StartupDiagnostics.Drain();
                if (warnings.Length > 0) MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, warnings), "KariyerTakip — Açılış uyarısı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
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
