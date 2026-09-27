using System.Text.Json;
using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Core.Fixtures;

public sealed record FixtureData(IReadOnlyList<QuotaSnapshot> Snapshots, IReadOnlyList<UsageSeries> History);

/// <summary>Reads portable fixture data without reflection-based serialization.</summary>
public static class FixtureFile
{
    public const int Version = 1;

    public static FixtureData Read(string json, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Invalid("json");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("version");
            if (Integer(Required(root, "version"), "version") != Version) throw Invalid("version");

            var snapshots = new List<QuotaSnapshot>();
            var snapshotArray = Required(root, "snapshots");
            if (snapshotArray.ValueKind != JsonValueKind.Array) throw Invalid("snapshots");
            foreach (var item in snapshotArray.EnumerateArray()) snapshots.Add(ReadSnapshot(item, now));

            var history = new List<UsageSeries>();
            if (root.TryGetProperty("history", out var historyArray))
            {
                if (historyArray.ValueKind != JsonValueKind.Array) throw Invalid("history");
                foreach (var item in historyArray.EnumerateArray()) history.Add(ReadSeries(item, now));
            }

            return new FixtureData(snapshots, history);
        }
    }

    private static QuotaSnapshot ReadSnapshot(JsonElement item, DateTimeOffset now)
    {
        if (item.ValueKind != JsonValueKind.Object) throw Invalid("snapshot");
        var provider = String(Required(item, "provider"), "provider");
        if (string.IsNullOrWhiteSpace(provider)) throw Invalid("provider");
        var healthText = String(Required(item, "health"), "health");
        if (!Enum.TryParse<HealthState>(healthText, ignoreCase: false, out var health) ||
            !Enum.IsDefined(health)) throw Invalid("health");
        var ageSeconds = item.TryGetProperty("ageSeconds", out var ageElement)
            ? Integer(ageElement, "ageSeconds") : 0;
        if (ageSeconds < 0) throw Invalid("ageSeconds");

        string? detail = null;
        if (item.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind != JsonValueKind.Null)
            detail = String(detailElement, "detail");

        var session = item.TryGetProperty("session", out var sessionElement) &&
                      sessionElement.ValueKind != JsonValueKind.Null
            ? ReadWindow(sessionElement, "session", now) : null;
        var weekly = item.TryGetProperty("weekly", out var weeklyElement) &&
                     weeklyElement.ValueKind != JsonValueKind.Null
            ? ReadWindow(weeklyElement, "weekly", now) : null;

        return new QuotaSnapshot(provider, session, weekly, health,
            AddSeconds(now, -ageSeconds, "ageSeconds"), detail);
    }

    private static QuotaWindow ReadWindow(JsonElement item, string field, DateTimeOffset now)
    {
        if (item.ValueKind != JsonValueKind.Object) throw Invalid(field);
        var percent = Number(Required(item, "percent"), "percent");
        if (percent < 0 || percent > 100) throw Invalid("percent");
        var resetsInMinutes = Integer(Required(item, "resetsInMinutes"), "resetsInMinutes");
        var windowMinutes = Integer(Required(item, "windowMinutes"), "windowMinutes");
        if (resetsInMinutes < 0) throw Invalid("resetsInMinutes");
        if (windowMinutes <= 0 || windowMinutes > 525600) throw Invalid("windowMinutes");
        return new QuotaWindow(percent, AddTime(now, resetsInMinutes, "resetsInMinutes"),
            TimeSpan.FromMinutes(windowMinutes));
    }

    private static UsageSeries ReadSeries(JsonElement item, DateTimeOffset now)
    {
        if (item.ValueKind != JsonValueKind.Object) throw Invalid("history");
        var provider = String(Required(item, "provider"), "provider");
        if (string.IsNullOrWhiteSpace(provider)) throw Invalid("provider");
        var kindText = String(Required(item, "kind"), "kind");
        if (!Enum.TryParse<WindowKind>(kindText, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            throw Invalid("kind");
        var windowMinutes = Integer(Required(item, "windowMinutes"), "windowMinutes");
        if (windowMinutes <= 0 || windowMinutes > 525600) throw Invalid("windowMinutes");
        DateTimeOffset? resetsAt = item.TryGetProperty("resetsInMinutes", out var resets)
            ? AddTime(now, Integer(resets, "resetsInMinutes"), "resetsInMinutes") : null;
        var samplesElement = Required(item, "samples");
        if (samplesElement.ValueKind != JsonValueKind.Array) throw Invalid("samples");
        var samples = new List<UsageSample>();
        foreach (var pair in samplesElement.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) throw Invalid("samples");
            var ageMinutes = Number(pair[0], "samples");
            var percent = Number(pair[1], "samples");
            if (ageMinutes < 0 || percent < 0 || percent > 100) throw Invalid("samples");
            samples.Add(new UsageSample(AddTime(now, -ageMinutes, "samples"), percent));
        }
        if (samples.Count == 0) throw Invalid("samples");
        return new UsageSeries(provider, kind, TimeSpan.FromMinutes(windowMinutes), resetsAt, samples);
    }

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw Invalid(name);

    private static string String(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Invalid(field);

    private static int Integer(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value : throw Invalid(field);

    private static double Number(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value))
            throw Invalid(field);
        return value;
    }

    private static DateTimeOffset AddTime(DateTimeOffset origin, double minutes, string field)
    {
        try { return origin.AddMinutes(minutes); }
        catch (ArgumentOutOfRangeException) { throw Invalid(field); }
        catch (OverflowException) { throw Invalid(field); }
    }

    private static DateTimeOffset AddSeconds(DateTimeOffset origin, double seconds, string field)
    {
        try { return origin.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { throw Invalid(field); }
        catch (OverflowException) { throw Invalid(field); }
    }

    private static FormatException Invalid(string field) => new($"Invalid fixture field: {field}.");
}
