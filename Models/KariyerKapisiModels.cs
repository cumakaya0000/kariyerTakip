using System.Text.Json.Serialization;

namespace KariyerTakip.Models;

public class SearchIlanRequest
{
    [JsonPropertyName("krM_ID")]
    public int KrM_ID { get; set; } = 0;

    [JsonPropertyName("searchText")]
    public string SearchText { get; set; } = string.Empty;

    [JsonPropertyName("il")]
    public string Il { get; set; } = "0";

    [JsonPropertyName("ilanTuru")]
    public string IlanTuru { get; set; } = "0";
}

public class GetIseAlimPageResponse
{
    [JsonPropertyName("searchIlan")]
    public List<SearchIlanItem>? SearchIlan { get; set; }
}

public class SearchIlanItem
{
    public AnnouncementSource Source { get; set; } = AnnouncementSource.CareerGate;
    public string DetailUrl { get; set; } = string.Empty;
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("kurumAdi")]
    public string KurumAdi { get; set; } = string.Empty;

    [JsonPropertyName("birimAdi")]
    public string BirimAdi { get; set; } = string.Empty;

    [JsonPropertyName("ilanBaslik")]
    public string IlanBaslik { get; set; } = string.Empty;

    [JsonPropertyName("ilanTipi")]
    public int IlanTipi { get; set; }

    [JsonPropertyName("ilanTuru")]
    public string IlanTuru { get; set; } = string.Empty;

    [JsonPropertyName("sonDurumu")]
    public string SonDurumu { get; set; } = string.Empty;

    [JsonPropertyName("basTarih")]
    public DateTime? BasTarih { get; set; }

    [JsonPropertyName("bitTarih")]
    public DateTime? BitTarih { get; set; }

    [JsonPropertyName("logo_Path")]
    public string LogoPath { get; set; } = string.Empty;

    [JsonPropertyName("basvuruLinki")]
    public string BasvuruLinki { get; set; } = string.Empty;
}

public class IlanGuidRequest
{
    [JsonPropertyName("ilanGuid")]
    public string IlanGuid { get; set; } = string.Empty;
}

public class IlanPreviewResponse
{
    [JsonPropertyName("ilN_NO")]
    public string? IlN_NO { get; set; }

    [JsonPropertyName("kurumAdi")]
    public string? KurumAdi { get; set; }

    [JsonPropertyName("birimAdi")]
    public string? BirimAdi { get; set; }

    [JsonPropertyName("ilanBaslik")]
    public string? IlanBaslik { get; set; }

    [JsonPropertyName("ilanMetni")]
    public string? IlanMetni { get; set; }

    [JsonPropertyName("ilanTuru")]
    public string? IlanTuru { get; set; }

    [JsonPropertyName("basTarih")]
    public DateTime? BasTarih { get; set; }

    [JsonPropertyName("bitTarih")]
    public DateTime? BitTarih { get; set; }

    [JsonPropertyName("basvuruLinki")]
    public string? BasvuruLinki { get; set; }

    [JsonPropertyName("eDevletServisURL")]
    public string? EDevletServisURL { get; set; }

    [JsonPropertyName("eDevletteGorunsun")]
    public int? EDevletteGorunsun { get; set; }
}

public class AltIlanResponse
{
    [JsonPropertyName("ilanBaslik")]
    public string? IlanBaslik { get; set; }

    [JsonPropertyName("ilanMetni")]
    public string? IlanMetni { get; set; }

    [JsonPropertyName("unvan")]
    public string? Unvan { get; set; }

    [JsonPropertyName("hizmetSinifi")]
    public string? HizmetSinifi { get; set; }

    [JsonPropertyName("kadroDerecesi")]
    public string? KadroDerecesi { get; set; }

    [JsonPropertyName("kontenjanList")]
    public List<KontenjanItem>? KontenjanList { get; set; }

    [JsonPropertyName("degerlemeAsamaList")]
    public List<DegerlemeAsamaItem>? DegerlemeAsamaList { get; set; }
}

public class KontenjanItem
{
    [JsonPropertyName("il")]
    public string? Il { get; set; }

    [JsonPropertyName("kontenjan")]
    public int Kontenjan { get; set; }
}

public class DegerlemeAsamaItem
{
    [JsonPropertyName("asamaAdi")]
    public string? AsamaAdi { get; set; }

    [JsonPropertyName("turu")]
    public string? Turu { get; set; }

    [JsonPropertyName("agirlik")]
    public double? Agirlik { get; set; }
}
