using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using Xunit;

namespace LimitTray.Tests.Codex;

public class CodexRolloutReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "aqt-tests-" + Guid.NewGuid().ToString("N"));

    public CodexRolloutReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteRollout(string name, string content, DateTime lastWrite)
    {
        var dir = Path.Combine(_root, "2026", "09", "03");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    [Fact]
    public void ReadLatest_ReadsSnakeCaseRateLimits_AsStale()
    {
        WriteRollout("rollout-a.jsonl", """
        {"type":"event"}
        {"rate_limits":{"limit_id":"codex","primary":{"used_percent":1.0,"window_minutes":300,"resets_at":1787753045},"secondary":{"used_percent":42.0,"window_minutes":10080,"resets_at":1788817184}}}
        """, new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.NotNull(snap);
        Assert.Equal(HealthState.Stale, snap!.Health);
        Assert.Equal(1.0, snap.Session!.Percent);
        Assert.Equal(42.0, snap.Weekly!.Percent);
    }

    [Fact]
    public void ReadLatest_PrefersMostRecentlyWrittenFile()
    {
        WriteRollout("rollout-old.jsonl",
            """{"rate_limits":{"primary":{"used_percent":11.0}}}""",
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));
        WriteRollout("rollout-new.jsonl",
            """{"rate_limits":{"primary":{"used_percent":22.0}}}""",
            new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.Equal(22.0, snap!.Session!.Percent);
    }

    [Fact]
    public void ReadLatest_UsesLastRateLimitsLineInFile()
    {
        WriteRollout("rollout-a.jsonl", """
        {"rate_limits":{"primary":{"used_percent":5.0}}}
        {"rate_limits":{"primary":{"used_percent":9.0}}}
        """, new DateTime(2026, 9, 3, 19, 0, 0, DateTimeKind.Utc));

        var snap = CodexRolloutReader.ReadLatest(_root, Now);

        Assert.Equal(9.0, snap!.Session!.Percent);
    }

    [Fact]
    public void ReadLatest_NoFiles_ReturnsNull() =>
        Assert.Null(CodexRolloutReader.ReadLatest(_root, Now));

    [Fact]
    public void ReadLatest_MissingDirectory_ReturnsNull() =>
        Assert.Null(CodexRolloutReader.ReadLatest(
            Path.Combine(_root, "yok"), Now));
}
