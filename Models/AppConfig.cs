namespace KariyerTakip.Models;

public class AppConfig
{
    public string ApiBaseUrl { get; set; } = "https://api.kariyerkapisi.gov.tr/api";
    public string PortalBaseUrl { get; set; } = "https://kariyerkapisi.gov.tr";
    public string DatabasePath { get; set; } = "kariyertakip.db";
    public TelegramOptions Telegram { get; set; } = new();
    public ScanOptions Scan { get; set; } = new();
}

public class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
}

public class ScanOptions
{
    public string SearchKeyword { get; set; } = string.Empty;
    public bool IncludeNeedsReview { get; set; } = true;
    public int RequestDelayMs { get; set; } = 300;
}
