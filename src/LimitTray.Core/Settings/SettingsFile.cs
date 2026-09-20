using System.Buffers;
using System.Text.Json;

namespace LimitTray.Core.Settings;

/// <summary>
/// JSON for <see cref="AppSettings"/>. Only setting values are written; unknown keys
/// are ignored on read so a newer file survives an older build, and missing keys take
/// their defaults so an older file survives a newer build.
/// </summary>
public static class SettingsFile
{
    public const int Version = 1;

    public static string Write(AppSettings s)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteString("theme", s.Theme.ToString());
            w.WriteString("language", s.Language.ToString());
            w.WriteBoolean("glassEffect", s.GlassEffect);
            w.WriteNumber("refreshSeconds", s.RefreshSeconds);
            w.WriteNumber("cautionPercent", s.Thresholds.Caution);
            w.WriteNumber("warningPercent", s.Thresholds.Warning);
            w.WriteBoolean("notifications", s.Notifications);
            w.WriteString("trayStyle", s.TrayStyle.ToString());
            w.WriteString("traySource", s.TraySource.ToString());
            w.WriteStartArray("expandedProviders");
            foreach (var p in s.ExpandedProviders.Order(StringComparer.Ordinal)) w.WriteStringValue(p);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Null for anything that is not a version-1 settings object.</summary>
    public static AppSettings? Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number
                || v.GetInt32() != Version) return null;

            var d = AppSettings.Default;
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("expandedProviders", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) expanded.Add(item.GetString()!);

            return new AppSettings(
                Enum(root, "theme", d.Theme),
                Enum(root, "language", d.Language),
                Bool(root, "glassEffect", d.GlassEffect),
                Int(root, "refreshSeconds", d.RefreshSeconds),
                new QuotaThresholds(
                    Double(root, "cautionPercent", d.Thresholds.Caution),
                    Double(root, "warningPercent", d.Thresholds.Warning)),
                Bool(root, "notifications", d.Notifications),
                Enum(root, "trayStyle", d.TrayStyle),
                Enum(root, "traySource", d.TraySource),
                expanded).Normalised();
        }
    }

    private static T Enum<T>(JsonElement root, string key, T fallback) where T : struct, System.Enum =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String
        && System.Enum.TryParse<T>(e.GetString(), ignoreCase: true, out var parsed)
        && System.Enum.IsDefined(parsed)
            ? parsed : fallback;

    private static bool Bool(JsonElement root, string key, bool fallback) =>
        root.TryGetProperty(key, out var e)
        && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;

    private static int Int(JsonElement root, string key, int fallback) =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number
        && e.TryGetInt32(out var n) ? n : fallback;

    private static double Double(JsonElement root, string key, double fallback) =>
        root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;
}
