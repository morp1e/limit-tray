using AgentQuotaTray.Core.Model;
using AgentQuotaTray.Core.Presentation;
using Xunit;

namespace AgentQuotaTray.Tests.Presentation;

public class QuotaFormatterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.0, QuotaSeverity.Normal)]
    [InlineData(59.9, QuotaSeverity.Normal)]
    [InlineData(60.0, QuotaSeverity.Caution)]
    [InlineData(85.0, QuotaSeverity.Caution)]
    [InlineData(85.1, QuotaSeverity.Warning)]
    [InlineData(100.0, QuotaSeverity.Warning)]
    public void SeverityFor_UsesFixedThresholds(double percent, QuotaSeverity expected) =>
        Assert.Equal(expected, QuotaFormatter.SeverityFor(percent));

    [Theory]
    [InlineData(14.0, "%14")]
    [InlineData(0.0, "%0")]
    [InlineData(36.4, "%36")]
    [InlineData(99.5, "%100")]
    public void Percent_RoundsToWholeNumber(double value, string expected) =>
        Assert.Equal(expected, QuotaFormatter.Percent(value));

    [Fact]
    public void ResetsIn_HoursAndMinutes() =>
        Assert.Equal("4s 38d sonra sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddMinutes(278), Now));

    [Fact]
    public void ResetsIn_DaysAndHours() =>
        Assert.Equal("1g 14s sonra sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddHours(38), Now));

    [Fact]
    public void ResetsIn_UnderOneMinute() =>
        Assert.Equal("birazdan sifirlanir",
            QuotaFormatter.ResetsIn(Now.AddSeconds(30), Now));

    [Fact]
    public void ResetsIn_PastTime_SaysResetting() =>
        Assert.Equal("sifirlaniyor", QuotaFormatter.ResetsIn(Now.AddMinutes(-5), Now));

    [Fact]
    public void ResetsIn_Null_SaysUnknown() =>
        Assert.Equal("sifirlanma zamani bilinmiyor", QuotaFormatter.ResetsIn(null, Now));

    [Fact]
    public void Age_JustNow() =>
        Assert.Equal("simdi guncellendi", QuotaFormatter.Age(Now.AddSeconds(-10), Now));

    [Fact]
    public void Age_Minutes() =>
        Assert.Equal("3 dk once", QuotaFormatter.Age(Now.AddMinutes(-3), Now));

    [Theory]
    [InlineData(HealthState.RateLimited, "Gecici olarak sinirli")]
    [InlineData(HealthState.AuthMissing, "Giris gerekli")]
    [InlineData(HealthState.ProtocolBroken, "API degismis")]
    public void HealthText_DescribesFailure(HealthState health, string expected)
    {
        var snapshot = QuotaSnapshot.Unhealthy("claude", health, Now, "detay");
        Assert.Equal(expected, QuotaFormatter.HealthText(snapshot));
    }

    [Fact]
    public void Tooltip_ShowsBothProviders()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude",
                new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                new QuotaWindow(12, null, TimeSpan.FromDays(7)),
                HealthState.Fresh, Now, null),
            new QuotaSnapshot("codex",
                new QuotaWindow(0, null, TimeSpan.FromHours(5)),
                new QuotaWindow(36, null, TimeSpan.FromDays(7)),
                HealthState.Fresh, Now, null),
        };

        Assert.Equal("CC %14 / %12  ·  CX %0 / %36",
            QuotaFormatter.Tooltip(snapshots, Now));
    }

    [Fact]
    public void Tooltip_UnhealthyProviderShowsReasonNotZero()
    {
        var snapshots = new[]
        {
            QuotaSnapshot.Unhealthy("claude", HealthState.AuthMissing, Now, "detay"),
        };

        var tooltip = QuotaFormatter.Tooltip(snapshots, Now);

        Assert.Contains("Giris gerekli", tooltip);
        Assert.DoesNotContain("%0", tooltip);
    }

    [Fact]
    public void HighestPercent_IgnoresUnhealthySnapshots()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Fresh, Now, null),
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.Equal(14.0, QuotaFormatter.HighestPercent(snapshots));
    }

    [Fact]
    public void HighestPercent_AllUnhealthy_ReturnsNull()
    {
        var snapshots = new[]
        {
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.Null(QuotaFormatter.HighestPercent(snapshots));
    }

    [Fact]
    public void HasUnhealthy_OneBrokenProviderAmongHealthy_IsTrue()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Fresh, Now, null),
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "detay"),
        };

        Assert.True(QuotaFormatter.HasUnhealthy(snapshots));
    }

    [Fact]
    public void HasUnhealthy_StaleIsNotUnhealthy()
    {
        var snapshots = new[]
        {
            new QuotaSnapshot("claude", new QuotaWindow(14, null, TimeSpan.FromHours(5)),
                null, HealthState.Stale, Now, null),
        };

        Assert.False(QuotaFormatter.HasUnhealthy(snapshots));
    }
}
