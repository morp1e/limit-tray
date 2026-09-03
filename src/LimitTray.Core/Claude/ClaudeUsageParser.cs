using System.Text.Json;
using LimitTray.Core.Model;

namespace LimitTray.Core.Claude;

public static class ClaudeUsageParser
{
    public const string Provider = "claude";

    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);

    public static QuotaSnapshot Parse(string json, DateTimeOffset now)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now, "Yanit JSON degil: " + ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var session = ReadWindow(root, "five_hour", FiveHours);
            var weekly = ReadWindow(root, "seven_day", SevenDays);

            // five_hour hicbir zaman opsiyonel degildir; yoksa sema degismistir.
            if (session is null && !HasExplicitNull(root, "five_hour"))
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "Yanitta five_hour alani yok");
            }

            return new QuotaSnapshot(Provider, session, weekly, HealthState.Fresh, now, null);
        }
    }

    private static bool HasExplicitNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Null;

    private static QuotaWindow? ReadWindow(JsonElement root, string name, TimeSpan length)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;

        if (!el.TryGetProperty("utilization", out var util) ||
            util.ValueKind != JsonValueKind.Number)
            return null;

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var reset) &&
            reset.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(reset.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            resetsAt = parsed;
        }

        return new QuotaWindow(util.GetDouble(), resetsAt, length);
    }
}
