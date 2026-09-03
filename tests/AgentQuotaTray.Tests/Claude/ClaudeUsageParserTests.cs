using AgentQuotaTray.Core.Claude;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Claude;

public class ClaudeUsageParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private const string RealResponse = """
    {"five_hour":{"utilization":14.0,"resets_at":"2026-09-03T23:09:59.727061+00:00"},
     "seven_day":{"utilization":12.0,"resets_at":"2026-09-05T08:59:59.727081+00:00"},
     "limits":[{"kind":"session","percent":14,"resets_at":"2026-09-03T23:09:59.727061+00:00"},
               {"kind":"weekly_all","percent":12,"resets_at":"2026-09-05T08:59:59.727081+00:00"}]}
    """;

    [Fact]
    public void Parse_RealResponse_ReadsBothWindows()
    {
        var snap = ClaudeUsageParser.Parse(RealResponse, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal("claude", snap.Provider);
        Assert.Equal(14.0, snap.Session!.Percent);
        Assert.Equal(12.0, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromHours(5), snap.Session.WindowLength);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly.WindowLength);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 23, 9, 59, TimeSpan.Zero),
            snap.Session.ResetsAt!.Value.TruncateToSeconds());
    }

    [Fact]
    public void Parse_ZeroPercent_IsFreshNotError()
    {
        var snap = ClaudeUsageParser.Parse(
            """{"five_hour":{"utilization":0.0,"resets_at":null},"seven_day":null}""", Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(0.0, snap.Session!.Percent);
        Assert.Null(snap.Weekly);
    }

    [Fact]
    public void Parse_MissingFiveHour_IsProtocolBroken()
    {
        var snap = ClaudeUsageParser.Parse("""{"seven_day":{"utilization":12.0}}""", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Null(snap.Session);
    }

    [Fact]
    public void Parse_Garbage_IsProtocolBroken()
    {
        var snap = ClaudeUsageParser.Parse("not json at all", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.NotNull(snap.Detail);
    }
}

internal static class DateTimeOffsetTestExtensions
{
    public static DateTimeOffset TruncateToSeconds(this DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second,
            value.Offset);
}
