using System.Text;
using LimitTray.Core.Codex;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Codex;

public class CodexRolloutTailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rt-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public CodexRolloutTailTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CodexRolloutTail Tail() => new(_root, () => Now);

    private string Day(string y, string m, string d)
    {
        var dir = Path.Combine(_root, y, m, d);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Line(string ts, double primary, double secondary) =>
        "{\"timestamp\":\"" + ts + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"," +
        "\"rate_limits\":{\"primary\":{\"used_percent\":" + primary.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"window_minutes\":300,\"resets_at\":1790479554},\"secondary\":{\"used_percent\":" +
        secondary.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"window_minutes\":10080,\"resets_at\":1791047422}}}}\n";

    private static string Filler(int bytes)
    {
        var sb = new StringBuilder();
        while (sb.Length < bytes) sb.Append("{\"timestamp\":\"2026-09-27T00:00:00Z\",\"type\":\"response_item\",\"payload\":{\"text\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}}\n");
        return sb.ToString();
    }

    [Fact]
    public void Poll_FirstCall_ReadsNewestFile_AsFreshWithLineTimestamp()
    {
        var dir = Day("2026", "09", "27");
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T01-40-28-a.jsonl"),
            Line("2026-09-27T11:59:00Z", 12.5, 40));

        var snap = Tail().Poll();

        Assert.NotNull(snap);
        Assert.Equal("codex", snap!.Provider);
        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(12.5, snap.Session!.Percent);
        Assert.Equal(40, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromMinutes(300), snap.Session.WindowLength);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 11, 59, 0, TimeSpan.Zero), snap.FetchedAt);
    }

    [Fact]
    public void Poll_WithoutGrowth_ReturnsNull_AfterAppend_ReturnsNewValue()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 10, 20));
        var tail = Tail();

        Assert.NotNull(tail.Poll());
        Assert.Null(tail.Poll());

        File.AppendAllText(path, Line("2026-09-27T11:30:00Z", 11, 21));
        var next = tail.Poll();
        Assert.Equal(11, next!.Session!.Percent);
    }

    [Fact]
    public void Poll_IncompleteLastLine_IsIgnored()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        var partial = Line("2026-09-27T11:40:00Z", 99, 99).TrimEnd('\n');
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 10, 20) + partial[..(partial.Length - 5)]);

        Assert.Equal(10, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_ReadsOnlyTheTail_OfALargeFile()
    {
        // The only reading sits before the last 64 KB; reading it would prove a full read.
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-40-28-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T10:00:00Z", 5, 5) + Filler(200 * 1024));

        Assert.Null(Tail().Poll());
    }

    [Fact]
    public void Poll_ConcurrentSessions_LatestLineTimestampWins()
    {
        var dir = Day("2026", "09", "27");
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T09-00-00-a.jsonl"), Line("2026-09-27T11:50:00Z", 30, 30));
        File.WriteAllText(Path.Combine(dir, "rollout-2026-09-27T10-00-00-b.jsonl"), Line("2026-09-27T11:10:00Z", 20, 20));

        Assert.Equal(30, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_AcrossMidnight_UsesThePreviousDayFolder()
    {
        File.WriteAllText(Path.Combine(Day("2026", "09", "26"), "rollout-2026-09-26T23-50-00-a.jsonl"), Line("2026-09-26T23:59:00Z", 7, 8));
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T00-01-00-b.jsonl"), Filler(1024));

        Assert.Equal(7, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_AcrossYearBoundary_FindsBothDays()
    {
        File.WriteAllText(Path.Combine(Day("2026", "12", "31"), "rollout-2026-12-31T23-00-00-a.jsonl"), Line("2026-12-31T23:30:00Z", 9, 9));
        File.WriteAllText(Path.Combine(Day("2027", "01", "01"), "rollout-2027-01-01T00-10-00-b.jsonl"), Filler(1024));

        Assert.Equal(9, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_FutureTimestamp_IsClampedToNow()
    {
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl"), Line("2026-09-28T00:00:00Z", 1, 1));

        Assert.Equal(Now, Tail().Poll()!.FetchedAt);
    }

    [Fact]
    public void Poll_MalformedOrOutOfRangeLines_AreSkipped()
    {
        var bad = "{\"timestamp\":\"2026-09-27T11:59:00Z\",\"rate_limits\":{\"primary\":{\"used_percent\":150}}}\n";
        var broken = "{\"timestamp\":\"2026-09-27T11:59:30Z\",\"rate_limits\":{\n";
        File.WriteAllText(Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl"),
            Line("2026-09-27T11:00:00Z", 15, 25) + bad + broken);

        Assert.Equal(15, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_FileHeldOpenForWriting_IsStillRead()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl");
        using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes(Line("2026-09-27T11:00:00Z", 33, 44));
        writer.Write(bytes); writer.Flush();

        Assert.Equal(33, Tail().Poll()!.Session!.Percent);
    }

    [Fact]
    public void Poll_ExclusivelyLockedFile_DoesNotThrow()
    {
        var path = Path.Combine(Day("2026", "09", "27"), "rollout-2026-09-27T01-00-00-a.jsonl");
        File.WriteAllText(path, Line("2026-09-27T11:00:00Z", 1, 1));
        using var locker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Record.Exception(() => Tail().Poll());
        Assert.Null(ex);
    }

    [Fact]
    public void Poll_MissingRoot_ReturnsNull() =>
        Assert.Null(new CodexRolloutTail(Path.Combine(_root, "nope"), () => Now).Poll());
}
