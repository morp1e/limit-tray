using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class ThemeTests
{
    [Theory]
    [InlineData("claude", QuotaSeverity.Normal, HealthState.Fresh, "D97757")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.Fresh, "10A37F")]
    [InlineData("other", QuotaSeverity.Normal, HealthState.Fresh, "50C878")]
    [InlineData("claude", QuotaSeverity.Caution, HealthState.Fresh, "E8B84A")]
    [InlineData("codex", QuotaSeverity.Warning, HealthState.Fresh, "E5484D")]
    [InlineData("claude", QuotaSeverity.Normal, HealthState.Stale, "6B7280")]
    [InlineData("claude", QuotaSeverity.Warning, HealthState.Stale, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.RateLimited, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.AuthMissing, "6B7280")]
    [InlineData("codex", QuotaSeverity.Normal, HealthState.ProtocolBroken, "6B7280")]
    public void ColourFor_FollowsTheSingleRule(string provider, QuotaSeverity severity, HealthState health, string hex)
    {
        var c = Theme.ColourFor(provider, severity, health);
        Assert.Equal(hex, $"{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    [Fact]
    public void OpacityFor_StaleIsDimmed()
    {
        Assert.Equal(255, Theme.OpacityFor(HealthState.Fresh));
        Assert.Equal(140, Theme.OpacityFor(HealthState.Stale));
    }

    [Theory]
    [InlineData(59.9, QuotaSeverity.Normal)]
    [InlineData(60, QuotaSeverity.Caution)]
    [InlineData(85, QuotaSeverity.Caution)]
    [InlineData(85.1, QuotaSeverity.Warning)]
    public void SeverityFor_DefaultThresholdsUnchanged(double percent, QuotaSeverity expected) =>
        Assert.Equal(expected, QuotaFormatter.SeverityFor(percent));

    [Fact]
    public void SeverityFor_HonoursCustomThresholds()
    {
        var t = new QuotaThresholds(70, 90);
        Assert.Equal(QuotaSeverity.Normal, QuotaFormatter.SeverityFor(65, t));
        Assert.Equal(QuotaSeverity.Caution, QuotaFormatter.SeverityFor(70, t));
        Assert.Equal(QuotaSeverity.Warning, QuotaFormatter.SeverityFor(91, t));
    }
}
