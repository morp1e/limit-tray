using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LimitTray.Core.Model;

namespace LimitTray.Core.Codex;

public sealed class CodexRolloutTail
{
    public const int TailBytes = 64 * 1024;
    public const int TrackedFiles = 4;

    private static readonly TimeSpan DefaultSessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan DefaultWeeklyWindow = TimeSpan.FromDays(7);
    private readonly string _sessionsRoot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, long> _lengths = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultSessionsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public CodexRolloutTail(string sessionsRoot, Func<DateTimeOffset> clock)
    {
        _sessionsRoot = sessionsRoot;
        _clock = clock;
    }

    public QuotaSnapshot? Poll()
    {
        try
        {
            var paths = FindTrackedFiles();
            var tracked = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            foreach (var oldPath in _lengths.Keys.Where(path => !tracked.Contains(path)).ToArray())
                _lengths.Remove(oldPath);

            QuotaSnapshot? latest = null;
            foreach (var path in paths)
            {
                var reading = ReadChangedFile(path);
                if (reading is not null &&
                    (latest is null || reading.FetchedAt > latest.FetchedAt))
                    latest = reading;
            }

            return latest;
        }
        catch
        {
            return null;
        }
    }

    private string[] FindTrackedFiles()
    {
        if (!Directory.Exists(_sessionsRoot)) return [];

        return NewestDayFolders(2)
            .SelectMany(day => Directory.GetFiles(day, "rollout-*.jsonl"))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Take(TrackedFiles)
            .ToArray();
    }

    /// <summary>
    /// Walks year, month and day folders newest first and stops as soon as it has enough
    /// days, so a poll normally lists three directories instead of every month on disk.
    /// </summary>
    private IEnumerable<string> NewestDayFolders(int count)
    {
        var found = 0;
        foreach (var year in NumberedFolders(_sessionsRoot, 4, 1, 9999))
        foreach (var month in NumberedFolders(year, 2, 1, 12))
        foreach (var day in NumberedFolders(month, 2, 1, 31))
        {
            yield return day;
            if (++found == count) yield break;
        }
    }

    /// <summary>Child folders named as a fixed-width number in range, newest (largest) first.</summary>
    private static IEnumerable<string> NumberedFolders(string parent, int width, int min, int max) =>
        Directory.GetDirectories(parent)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Length == width
                    && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    && value >= min && value <= max;
            })
            // Equal-width digit names sort numerically under ordinal comparison.
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);

    private QuotaSnapshot? ReadChangedFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (_lengths.TryGetValue(path, out var previousLength) && previousLength == length)
                return null;
            _lengths[path] = length;

            var count = (int)Math.Min(length, TailBytes);
            if (count == 0) return null;
            var offset = length - count;
            var buffer = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                stream.Position = offset;
                var read = 0;
                while (read < count)
                {
                    var amount = stream.Read(buffer, read, count - read);
                    if (amount == 0) break;
                    read += amount;
                }

                var start = 0;
                if (offset > 0)
                {
                    while (start < read && buffer[start] != (byte)'\n') start++;
                    if (start == read) return null;
                    start++;
                }

                var end = read;
                if (end <= start || buffer[end - 1] != (byte)'\n')
                {
                    while (end > start && buffer[end - 1] != (byte)'\n') end--;
                }

                if (end <= start) return null;

                // Lines are walked backwards in the byte buffer and only a line that
                // carries the marker is handed to the parser, still as UTF-8. Decoding the
                // whole tail to a string and splitting it cost about 256 KB of garbage per
                // changed file, every 30 seconds while Codex is in use.
                var data = buffer.AsMemory(start, end - start);
                var lineEnd = data.Length;
                while (lineEnd > 0)
                {
                    var contentEnd = data.Span[lineEnd - 1] == (byte)'\n' ? lineEnd - 1 : lineEnd;
                    var lineStart = data.Span[..contentEnd].LastIndexOf((byte)'\n') + 1;
                    var line = data[lineStart..contentEnd];
                    if (line.Span.IndexOf(RateLimitsMarker) >= 0)
                    {
                        var snapshot = ParseLine(line);
                        if (snapshot is not null) return snapshot;
                    }
                    lineEnd = lineStart;
                }

                return null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    private static ReadOnlySpan<byte> RateLimitsMarker => "\"rate_limits\""u8;

    /// <summary>The document borrows the pooled buffer; it is disposed before the buffer returns.</summary>
    private QuotaSnapshot? ParseLine(ReadOnlyMemory<byte> line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("timestamp", out var timestampElement) ||
                timestampElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(timestampElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ||
                !TryFindRateLimits(root, out var limits)) return null;

            var session = CodexRateLimitsParser.ReadWindow(limits, "primary", "used_percent",
                "window_minutes", "resets_at", DefaultSessionWindow);
            var weekly = CodexRateLimitsParser.ReadWindow(limits, "secondary", "used_percent",
                "window_minutes", "resets_at", DefaultWeeklyWindow);
            if (session is null && weekly is null) return null;
            if (!QuotaWindowRange.IsValid(session) || !QuotaWindowRange.IsValid(weekly)) return null;

            var now = _clock();
            return new QuotaSnapshot(CodexRateLimitsParser.Provider, session, weekly,
                HealthState.Fresh, timestamp > now ? now : timestamp, null);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryFindRateLimits(JsonElement root, out JsonElement limits)
    {
        if (root.TryGetProperty("rate_limits", out limits) &&
            limits.ValueKind == JsonValueKind.Object) return true;

        if (root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("rate_limits", out limits) &&
            limits.ValueKind == JsonValueKind.Object) return true;

        limits = default;
        return false;
    }
}
