using System.Text.Json;
using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Codex;

public static class CodexRateLimitsParser
{
    public const string Provider = "codex";

    private static readonly TimeSpan DefaultSessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan DefaultWeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>result.rateLimits veya params.rateLimits tasiyan mesaji ayristirir.</summary>
    public static QuotaSnapshot ParseAppServer(string json, DateTimeOffset now)
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
            if (!TryFindRateLimits(doc.RootElement, out var limits))
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "Yanitta rateLimits alani yok");
            }

            var session = ReadWindow(limits, "primary", "usedPercent",
                "windowDurationMins", "resetsAt", DefaultSessionWindow);
            var weekly = ReadWindow(limits, "secondary", "usedPercent",
                "windowDurationMins", "resetsAt", DefaultWeeklyWindow);

            if (session is null && weekly is null)
            {
                return QuotaSnapshot.Unhealthy(
                    Provider, HealthState.ProtocolBroken, now,
                    "rateLimits icinde pencere yok");
            }

            return new QuotaSnapshot(Provider, session, weekly, HealthState.Fresh, now, null);
        }
    }

    internal static QuotaWindow? ReadWindow(
        JsonElement parent, string name, string percentField, string windowField,
        string resetField, TimeSpan fallbackWindow)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty(percentField, out var pct) ||
            pct.ValueKind != JsonValueKind.Number)
            return null;

        var window = fallbackWindow;
        if (el.TryGetProperty(windowField, out var mins) &&
            mins.ValueKind == JsonValueKind.Number)
        {
            window = TimeSpan.FromMinutes(mins.GetDouble());
        }

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty(resetField, out var reset) &&
            reset.ValueKind == JsonValueKind.Number)
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(reset.GetInt64());
        }

        return new QuotaWindow(pct.GetDouble(), resetsAt, window);
    }

    private static bool TryFindRateLimits(JsonElement root, out JsonElement limits)
    {
        foreach (var container in new[] { "result", "params" })
        {
            if (root.TryGetProperty(container, out var el) &&
                el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("rateLimits", out limits) &&
                limits.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        if (root.TryGetProperty("rateLimits", out limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        limits = default;
        return false;
    }
}
