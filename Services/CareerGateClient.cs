using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;

namespace KariyerTakip.Services;

public class CareerGateClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly ILogger<CareerGateClient> _logger;

    public CareerGateClient(HttpClient httpClient, IOptions<AppConfig> config, ILogger<CareerGateClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;

        if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(_config.ApiBaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_config.ApiBaseUrl.TrimEnd('/') + "/");
        }

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", _config.PortalBaseUrl);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", _config.PortalBaseUrl + "/");
    }

    public async Task<List<SearchIlanItem>> GetActiveAnnouncementsAsync(string searchText = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var req = new SearchIlanRequest
            {
                KrM_ID = 0,
                SearchText = searchText,
                Il = "0",
                IlanTuru = "0"
            };

            var response = await _httpClient.PostAsJsonAsync("ilan/GetIseAlimPage", req, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GetIseAlimPageResponse>(cancellationToken: cancellationToken);
            var list = result?.SearchIlan ?? new List<SearchIlanItem>();

            // Filter active by date
            var now = DateTime.Now;
            var activeList = list.Where(x => !x.BitTarih.HasValue || x.BitTarih.Value >= now).ToList();

            _logger.LogInformation("Kariyer Kapısı'ndan toplam {Total} ilan çekildi, {Active} tanesi aktif.", list.Count, activeList.Count);
            return activeList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kariyer Kapısı ilan listesi alınırken hata oluştu.");
            throw;
        }
    }

    public async Task<IlanPreviewResponse?> GetAnnouncementPreviewAsync(string guid, CancellationToken cancellationToken = default)
    {
        try
        {
            var req = new IlanGuidRequest { IlanGuid = guid };
            var response = await _httpClient.PostAsJsonAsync("ilan/GetIlanPreviewPublic", req, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogWarning("İlan önizlemesi bulunamadı (204 No Content): {Guid}", guid);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<IlanPreviewResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İlan önizleme bilgisi alınırken hata oluştu (Guid: {Guid})", guid);
            return null;
        }
    }

    public async Task<List<AltIlanResponse>> GetPositionsAsync(string guid, CancellationToken cancellationToken = default)
    {
        try
        {
            var req = new IlanGuidRequest { IlanGuid = guid };
            var response = await _httpClient.PostAsJsonAsync("altilan/GetAltIlanInfoByIlanIdPublic", req, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return new List<AltIlanResponse>();
            }

            response.EnsureSuccessStatusCode();
            var list = await response.Content.ReadFromJsonAsync<List<AltIlanResponse>>(cancellationToken: cancellationToken);
            return list ?? new List<AltIlanResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kadro (alt ilan) bilgileri alınırken hata oluştu (Guid: {Guid})", guid);
            return new List<AltIlanResponse>();
        }
    }
}
