using System.Text.Json;
using LimitTray.Core.Model;

namespace LimitTray.Core.Codex;

/// <summary>
/// Reads the last known quota from rollout files when the app-server is not running.
/// This source is always Stale: the file is as old as the last API call.
/// </summary>
public static class CodexRolloutReader
{
    private static readonly TimeSpan DefaultSessionWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan DefaultWeeklyWindow = TimeSpan.FromDays(7);

    public static string DefaultSessionsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    public static QuotaSnapshot? ReadLatest(string sessionsRoot, DateTimeOffset now)
    {
        if (!Directory.Exists(sessionsRoot)) return null;

        string[] files;
        try
        {
            files = Directory.GetFiles(sessionsRoot, "rollout-*.jsonl",
                SearchOption.AllDirectories);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        foreach (var path in files.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var snapshot = ReadFile(path, now);
            if (snapshot is not null) return snapshot;
        }

        return null;
    }

    private static QuotaSnapshot? ReadFile(string path, DateTimeOffset now)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!TryFind(doc.RootElement, out var limits)) continue;

                var session = CodexRateLimitsParser.ReadWindow(limits, "primary",
                    "used_percent", "window_minutes", "resets_at", DefaultSessionWindow);
                var weekly = CodexRateLimitsParser.ReadWindow(limits, "secondary",
                    "used_percent", "window_minutes", "resets_at", DefaultWeeklyWindow);

                if (session is null && weekly is null) continue;

                // The value is as old as the file, not as old as this read. Stamping it
                // with `now` makes the panel say "updated just now" directly under "data
                // is stale" -- two statements that cannot both be true. The file write
                // time is the closest honest answer available. A clock skew that puts it
                // in the future is clamped back to now.
                var written = FileWriteTime(path);
                var fetchedAt = written is { } stamp && stamp < now ? stamp : now;

                return new QuotaSnapshot(
                    CodexRateLimitsParser.Provider, session, weekly,
                    HealthState.Stale, fetchedAt,
                    "app-server yok, son bilinen deger dosyadan okundu");
            }
        }

        return null;
    }

    private static DateTimeOffset? FileWriteTime(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryFind(JsonElement root, out JsonElement limits)
    {
        if (root.TryGetProperty("rate_limits", out limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("rate_limits", out limits) &&
                limits.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        limits = default;
        return false;
    }
}
