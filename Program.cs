using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KariyerTakip.Forms;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;

namespace KariyerTakip;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = Host.CreateApplicationBuilder(args);

        // 1. Configuration files
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("profile.json", optional: false, reloadOnChange: true)
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

        // 5. Register GUI Form
        builder.Services.AddSingleton<MainForm>();

        var host = builder.Build();

        // 6. Run GUI Application
        var mainForm = host.Services.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }
}
