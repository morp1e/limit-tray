using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using Xunit;

namespace LimitTray.Tests.Codex;

public class CodexRateLimitsParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    // Actual response measured from the app-server.
    private const string RealResult = """
    {"id":2,"result":{"rateLimits":{"limitId":"codex",
      "primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788478826},
      "secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1788817184},
      "planType":"plus"}}}
    """;

    [Fact]
    public void ParseAppServer_RealResult_ReadsBothWindows()
    {
        var snap = CodexRateLimitsParser.ParseAppServer(RealResult, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal("codex", snap.Provider);
        Assert.Equal(0.0, snap.Session!.Percent);
        Assert.Equal(36.0, snap.Weekly!.Percent);
        Assert.Equal(TimeSpan.FromMinutes(300), snap.Session.WindowLength);
        Assert.Equal(TimeSpan.FromMinutes(10080), snap.Weekly.WindowLength);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1788478826), snap.Session.ResetsAt);
    }

    [Fact]
    public void ParseAppServer_NotificationShape_IsAlsoAccepted()
    {
        const string notification = """
        {"method":"account/rateLimits/updated","params":{"rateLimits":{
          "primary":{"usedPercent":7,"windowDurationMins":300,"resetsAt":1788478826}}}}
        """;

        var snap = CodexRateLimitsParser.ParseAppServer(notification, Now);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(7.0, snap.Session!.Percent);
        Assert.Null(snap.Weekly);
    }

    [Fact]
    public void ParseAppServer_MissingRateLimits_IsProtocolBroken()
    {
        var snap = CodexRateLimitsParser.ParseAppServer("""{"id":2,"result":{}}""", Now);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
    }

    [Fact]
    public void ParseAppServer_WindowDurationMissing_FallsBackToKnownWindows()
    {
        const string json = """
        {"result":{"rateLimits":{"primary":{"usedPercent":5},
                                 "secondary":{"usedPercent":9}}}}
        """;

        var snap = CodexRateLimitsParser.ParseAppServer(json, Now);

        Assert.Equal(TimeSpan.FromHours(5), snap.Session!.WindowLength);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly!.WindowLength);
    }
}
