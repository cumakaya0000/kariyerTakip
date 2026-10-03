using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KariyerTakip.Common;

public static class AppTime
{
    public static TimeZoneInfo PortalTimeZone { get; } = ResolvePortalTimeZone(TimeZoneInfo.FindSystemTimeZoneById);
    public static TimeZoneInfo ResolvePortalTimeZone(Func<string, TimeZoneInfo> lookup)
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return lookup(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        // Turkey currently uses UTC+3 year-round; this also supports invariant runtimes.
        return TimeZoneInfo.CreateCustomTimeZone("Turkey-UTC+3", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }
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
