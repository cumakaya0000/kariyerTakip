using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KariyerTakip.Common;

public static class AppTime
{
    public static TimeZoneInfo PortalTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    public static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? TimeZoneInfo.ConvertTimeToUtc(value, PortalTimeZone) : value.ToUniversalTime();
    public static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;
    public static DateTime ToDisplay(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(ToUtc(value), PortalTimeZone);
    public static string Format(DateTime? value, string fallback = "Belirtilmemiş") =>
        value.HasValue ? ToDisplay(value.Value).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : fallback;
    public static DateTime ParseUtc(string value, bool portalDate = false)
    {
        var parsed = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return parsed.Kind == DateTimeKind.Unspecified && !portalDate
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : ToUtc(parsed);
    }
}

public sealed class PortalDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        AppTime.ToUtc(reader.GetDateTime());
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(AppTime.ToUtc(value));
}
