using LimitTray.Core.History;
using LimitTray.Core.Model;
using Xunit;

namespace LimitTray.Tests.History;

public class HistoryFileTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static UsageHistory Populated(string? detail = null)
    {
        var history = new UsageHistory();
        for (var i = 0; i < 4; i++)
        {
            history.Observe(new QuotaSnapshot(
                "claude",
                new QuotaWindow(10 + i, Start.AddHours(4), TimeSpan.FromHours(5)),
                new QuotaWindow(50 + i, null, TimeSpan.FromDays(7)),
                HealthState.Fresh, Start.AddMinutes(10 * i), detail));
        }
        return history;
    }

    [Fact]
    public void RoundTrip_PreservesSamplesAndWindowMetadata()
    {
        var written = HistoryFile.Write(Populated());
        var read = HistoryFile.Read(written);

        Assert.NotNull(read);

        var session = read!.Samples("claude", WindowKind.Session);
        Assert.Equal(4, session.Count);
        Assert.Equal(Start, session[0].At);
        Assert.Equal(10, session[0].Percent);
        Assert.Equal(13, session[3].Percent);

        var snapshot = read.LastKnown("claude");
        Assert.Equal(Start.AddMinutes(30), snapshot!.FetchedAt);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.Session!.WindowLength);
        Assert.Equal(Start.AddHours(4), snapshot.Session.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(7), snapshot.Weekly!.WindowLength);
        Assert.Null(snapshot.Weekly.ResetsAt);
    }

    [Fact]
    public void RoundTrip_KeepsTheProjectionIntact()
    {
        var history = new UsageHistory();
        for (var i = 0; i < 5; i++)
        {
            history.Observe(new QuotaSnapshot(
                "claude", new QuotaWindow(10 + 2.5 * i, null, TimeSpan.FromHours(5)),
                null, HealthState.Fresh, Start.AddMinutes(15 * i), null));
        }

        var before = history.Estimate("claude", WindowKind.Session, Start.AddHours(1));
        var after = HistoryFile.Read(HistoryFile.Write(history))!
            .Estimate("claude", WindowKind.Session, Start.AddHours(1));

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.PercentPerHour, after!.PercentPerHour, 6);
        Assert.Equal(before.TimeToFull, after.TimeToFull);
    }

    [Fact]
    public void Write_NeverContainsTheSnapshotDetail()
    {
        // Detail can carry an exception message. Nothing but percentages, window
        // lengths and timestamps is allowed onto disk.
        var written = HistoryFile.Write(Populated("sk-ant-oat01-do-not-persist-this"));

        Assert.DoesNotContain("sk-ant", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("detail", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("author", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_ProducesOnlyTheExpectedFields()
    {
        var written = HistoryFile.Write(Populated());
        using var doc = System.Text.Json.JsonDocument.Parse(written);

        var names = doc.RootElement.GetProperty("series")[0]
            .EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
                { "provider", "window", "windowMinutes", "resetsAt", "samples" },
            names);
    }

    [Fact]
    public void Read_EmptyHistory_RoundTripsToEmpty()
    {
        var read = HistoryFile.Read(HistoryFile.Write(new UsageHistory()));

        Assert.NotNull(read);
        Assert.Empty(read!.Providers());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"version\":1,\"series\":[")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"series\":[]}")]
    [InlineData("{\"version\":\"1\",\"series\":[]}")]
    [InlineData("{\"version\":99,\"series\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"series\":{}}")]
    public void Read_UnusableDocument_IsNull(string json) =>
        Assert.Null(HistoryFile.Read(json));

    [Fact]
    public void Read_SkipsUnusableSeriesButKeepsTheRest()
    {
        var json = """
        {"version":1,"series":[
          {"provider":"claude","window":"nonsense","samples":[{"at":"2026-09-04T12:00:00.0000000+00:00","percent":5}]},
          {"provider":"codex","window":"session","windowMinutes":300,"samples":[{"at":"2026-09-04T12:00:00.0000000+00:00","percent":5}]}
        ]}
        """;

        var read = HistoryFile.Read(json);

        Assert.NotNull(read);
        Assert.Equal(new[] { "codex" }, read!.Providers());
    }

    [Fact]
    public void Read_RejectsImpossiblePercentages()
    {
        var json = """
        {"version":1,"series":[
          {"provider":"codex","window":"session","windowMinutes":300,"samples":[
            {"at":"2026-09-04T12:00:00.0000000+00:00","percent":-5},
            {"at":"2026-09-04T12:10:00.0000000+00:00","percent":140},
            {"at":"2026-09-04T12:20:00.0000000+00:00","percent":42}
          ]}
        ]}
        """;

        var samples = HistoryFile.Read(json)!.Samples("codex", WindowKind.Session);

        Assert.Single(samples);
        Assert.Equal(42, samples[0].Percent);
    }
}
