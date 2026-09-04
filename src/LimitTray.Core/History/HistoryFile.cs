using System.Buffers;
using System.Globalization;
using System.Text.Json;
using LimitTray.Core.Model;

namespace LimitTray.Core.History;

/// <summary>
/// Turns a <see cref="UsageHistory"/> into JSON and back.
///
/// The written document contains percentages, window lengths and timestamps and
/// nothing else. In particular the snapshot Detail text is never written: it can
/// carry an exception message, and this file lives on disk.
/// </summary>
public static class HistoryFile
{
    /// <summary>Bumped when the shape changes. An unknown version reads as a first run.</summary>
    public const int Version = 1;

    public static string Write(UsageHistory history)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("series");

            foreach (var series in history.Export())
            {
                writer.WriteStartObject();
                writer.WriteString("provider", series.Provider);
                writer.WriteString("window", series.Kind == WindowKind.Session ? "session" : "weekly");
                writer.WriteNumber("windowMinutes", series.WindowLength.TotalMinutes);

                if (series.ResetsAt is { } resetsAt)
                    writer.WriteString("resetsAt", resetsAt.ToString("O", CultureInfo.InvariantCulture));

                writer.WriteStartArray("samples");
                foreach (var sample in series.Samples)
                {
                    writer.WriteStartObject();
                    writer.WriteString("at", sample.At.ToString("O", CultureInfo.InvariantCulture));
                    writer.WriteNumber("percent", sample.Percent);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Parses a document written by <see cref="Write"/>. Returns null for anything that
    /// is not recognisably one: truncated, corrupt, or from a future version. A caller
    /// that gets null must behave exactly as it would on a first run.
    /// </summary>
    public static UsageHistory? Read(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            try
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                if (!root.TryGetProperty("version", out var version) ||
                    version.ValueKind != JsonValueKind.Number ||
                    version.GetInt32() != Version)
                {
                    return null;
                }

                if (!root.TryGetProperty("series", out var seriesArray) ||
                    seriesArray.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var history = new UsageHistory();
                foreach (var element in seriesArray.EnumerateArray())
                {
                    var series = ReadSeries(element);
                    if (series is not null) history.Import(series);
                }

                return history;
            }
            catch (Exception ex) when (ex is JsonException or FormatException
                                          or InvalidOperationException or OverflowException)
            {
                return null;
            }
        }
    }

    private static UsageSeries? ReadSeries(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (!element.TryGetProperty("provider", out var providerElement) ||
            providerElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var provider = providerElement.GetString();
        if (string.IsNullOrWhiteSpace(provider)) return null;

        if (!element.TryGetProperty("window", out var windowElement) ||
            windowElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var kind = windowElement.GetString() switch
        {
            "session" => (WindowKind?)WindowKind.Session,
            "weekly" => WindowKind.Weekly,
            _ => null,
        };
        if (kind is null) return null;

        var windowLength = TimeSpan.Zero;
        if (element.TryGetProperty("windowMinutes", out var minutes) &&
            minutes.ValueKind == JsonValueKind.Number)
        {
            var value = minutes.GetDouble();
            if (double.IsFinite(value) && value >= 0 && value <= 525600)
                windowLength = TimeSpan.FromMinutes(value);
        }

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resetsAt", out var reset) &&
            reset.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedReset))
        {
            resetsAt = parsedReset;
        }

        if (!element.TryGetProperty("samples", out var samplesArray) ||
            samplesArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var samples = new List<UsageSample>();
        foreach (var sampleElement in samplesArray.EnumerateArray())
        {
            var sample = ReadSample(sampleElement);
            if (sample is not null) samples.Add(sample.Value);
        }

        return samples.Count == 0
            ? null
            : new UsageSeries(provider!, kind.Value, windowLength, resetsAt, samples);
    }

    private static UsageSample? ReadSample(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (!element.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String)
            return null;
        if (!DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedAt))
        {
            return null;
        }

        if (!element.TryGetProperty("percent", out var percent) ||
            percent.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var value = percent.GetDouble();
        if (!double.IsFinite(value) || value < 0 || value > 100) return null;

        return new UsageSample(parsedAt, value);
    }
}
