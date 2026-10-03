using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Models;
using KariyerTakip.Common;

namespace KariyerTakip.Services;

public enum ApiCallStatus
{
    Success,
    EmptyResponse,
    HttpError,
    NetworkError,
    ParseError
}

public class ApiResult<T>
{
    public ApiCallStatus Status { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public int? StatusCode { get; set; }

    public bool IsSuccess => Status == ApiCallStatus.Success && Data != null;

    public static ApiResult<T> Ok(T data) => new() { Status = ApiCallStatus.Success, Data = data };
    public static ApiResult<T> Empty() => new() { Status = ApiCallStatus.EmptyResponse };
    public static ApiResult<T> Fail(ApiCallStatus status, string error, int? code = null) =>
        new() { Status = status, ErrorMessage = error, StatusCode = code };
}

public class CareerGateClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly ILogger<CareerGateClient> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTime _nextRequestUtc;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new PortalDateConverter());
        return options;
    }

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

    public async Task<ApiResult<List<SearchIlanItem>>> GetActiveAnnouncementsAsync(string searchText = "", CancellationToken cancellationToken = default)
    {
        var req = new SearchIlanRequest
        {
            KrM_ID = 0,
            SearchText = searchText,
            Il = "0",
            IlanTuru = "0"
        };

        return await ExecuteWithRetryAsync<GetIseAlimPageResponse, List<SearchIlanItem>>(
            "ilan/GetIseAlimPage",
            req,
            resp =>
            {
                var list = resp?.SearchIlan ?? new List<SearchIlanItem>();
                // Only return active postings
                var now = DateTime.UtcNow;
                return list.Where(x => !x.BitTarih.HasValue || x.BitTarih.Value >= now).ToList();
            },
            cancellationToken);
    }

    public async Task<ApiResult<IlanPreviewResponse>> GetAnnouncementPreviewAsync(string guid, CancellationToken cancellationToken = default)
    {
        var req = new IlanGuidRequest { IlanGuid = guid };
        return await ExecuteWithRetryAsync<IlanPreviewResponse, IlanPreviewResponse>(
            "ilan/GetIlanPreviewPublic",
            req,
            resp => resp,
            cancellationToken);
    }

    public async Task<ApiResult<List<AltIlanResponse>>> GetPositionsAsync(string guid, CancellationToken cancellationToken = default)
    {
        var req = new IlanGuidRequest { IlanGuid = guid };
        return await ExecuteWithRetryAsync<List<AltIlanResponse>, List<AltIlanResponse>>(
            "altilan/GetAltIlanInfoByIlanIdPublic",
            req,
            resp => resp ?? new List<AltIlanResponse>(),
            cancellationToken);
    }

    private async Task<ApiResult<TOut>> ExecuteWithRetryAsync<TIn, TOut>(
        string endpoint,
        object payload,
        Func<TIn?, TOut?> transform,
        CancellationToken cancellationToken,
        int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _requestGate.WaitAsync(cancellationToken);
                try
                {
                    var wait = _nextRequestUtc - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
                    _nextRequestUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(0, _config.Scan.RequestDelayMs));
                }
                finally { _requestGate.Release(); }
                using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return ApiResult<TOut>.Empty();
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("API geçici hata döndürdü ({StatusCode}). Deneme: {Attempt}/{MaxRetries}", response.StatusCode, attempt, maxRetries);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(RetryPolicy.GetDelay(attempt, response.Headers.RetryAfter), cancellationToken);
                        continue;
                    }
                    return ApiResult<TOut>.Fail(ApiCallStatus.HttpError, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}", (int)response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                    return ApiResult<TOut>.Fail(ApiCallStatus.HttpError, $"HTTP {(int)response.StatusCode}", (int)response.StatusCode);

                var rawObj = await response.Content.ReadFromJsonAsync<TIn>(JsonOptions, cancellationToken);
                if (rawObj == null) return ApiResult<TOut>.Empty();
                var resultData = transform(rawObj);

                if (resultData == null)
                    return ApiResult<TOut>.Empty();

                return ApiResult<TOut>.Ok(resultData);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Ağ hatası oluştu (Endpoint: {Endpoint}, Deneme: {Attempt}/{Max})", endpoint, attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    await Task.Delay(RetryPolicy.GetDelay(attempt), cancellationToken);
                    continue;
                }
                return ApiResult<TOut>.Fail(ApiCallStatus.NetworkError, ex.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON ayrıştırma hatası (Endpoint: {Endpoint})", endpoint);
                return ApiResult<TOut>.Fail(ApiCallStatus.ParseError, ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                if (attempt < maxRetries) { await Task.Delay(RetryPolicy.GetDelay(attempt), cancellationToken); continue; }
                return ApiResult<TOut>.Fail(ApiCallStatus.NetworkError, "API isteği zaman aşımına uğradı.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Beklenmeyen hata (Endpoint: {Endpoint})", endpoint);
                return ApiResult<TOut>.Fail(ApiCallStatus.NetworkError, ex.Message);
            }
        }

        return ApiResult<TOut>.Fail(ApiCallStatus.NetworkError, "Maksimum tekrar deneme sınırına ulaşıldı.");
    }
}
