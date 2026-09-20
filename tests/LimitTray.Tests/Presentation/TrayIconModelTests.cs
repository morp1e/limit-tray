using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class TrayIconModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static QuotaSnapshot Snap(string provider, double session, double weekly, HealthState health = HealthState.Fresh) =>
        new(provider,
            new QuotaWindow(session, Now.AddHours(1), TimeSpan.FromHours(5)),
            new QuotaWindow(weekly, Now.AddDays(1), TimeSpan.FromDays(7)),
            health, Now, null);

    private static AppSettings With(TrayIconStyle style, TrayIconSource source = TrayIconSource.Highest) =>
        AppSettings.Default with { TrayStyle = style, TraySource = source };

    [Fact]
    public void Ring_HighestPicksTheFullestWindowAcrossProviders()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) }, With(TrayIconStyle.Ring));

        Assert.Equal(TrayIconStyle.Ring, m.Style);
        Assert.Equal(52, m.Primary!.Percent);
        Assert.Equal(Palette.CodexBrand, m.Primary.Colour);
        Assert.False(m.HasUnhealthy);
    }

    [Fact]
    public void Number_ColourFollowsSeverity()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 93, 25) }, With(TrayIconStyle.Number));
        Assert.Equal(Palette.Warning, m.Primary!.Colour);
    }

    [Fact]
    public void SpecificSource_UsesThatWindowOnly()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) },
            With(TrayIconStyle.Ring, TrayIconSource.ClaudeWeekly));
        Assert.Equal(25, m.Primary!.Percent);
        Assert.Equal(Palette.ClaudeBrand, m.Primary.Colour);
    }

    [Fact]
    public void SpecificSource_UnhealthyProviderIsNotReplacedByAnotherProvider()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { QuotaSnapshot.Unhealthy("claude", HealthState.RateLimited, Now, "429"), Snap("codex", 39, 52) },
            With(TrayIconStyle.Ring, TrayIconSource.ClaudeSession));
        Assert.Null(m.Primary);
        Assert.True(m.HasUnhealthy);
    }

    [Fact]
    public void Highest_IgnoresUnhealthyButKeepsStale()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { QuotaSnapshot.Unhealthy("claude", HealthState.AuthMissing, Now, "x"), Snap("codex", 39, 52, HealthState.Stale) },
            With(TrayIconStyle.Ring));
        Assert.Equal(52, m.Primary!.Percent);
        Assert.Equal(Palette.Muted, m.Primary.Colour);
        Assert.Equal(140, m.Primary.Opacity);
        Assert.True(m.HasUnhealthy);
    }

    [Fact]
    public void NoData_PrimaryIsNull()
    {
        var m = TrayIconModelBuilder.Build(Array.Empty<QuotaSnapshot>(), With(TrayIconStyle.Number));
        Assert.Null(m.Primary);
        Assert.False(m.HasUnhealthy);
    }

    [Fact]
    public void DualBar_LeftClaudeRightCodex_SourceIgnored()
    {
        var m = TrayIconModelBuilder.Build(
            new[] { Snap("claude", 50, 25), Snap("codex", 39, 52) },
            With(TrayIconStyle.DualBar, TrayIconSource.CodexWeekly));
        Assert.Equal(50, m.Left!.Percent);
        Assert.Equal(Palette.ClaudeBrand, m.Left.Colour);
        Assert.Equal(52, m.Right!.Percent);
        Assert.Equal(Palette.CodexBrand, m.Right.Colour);
        Assert.Null(m.Primary);
    }

    [Fact]
    public void DualBar_MissingProviderSideIsNull()
    {
        var m = TrayIconModelBuilder.Build(new[] { Snap("codex", 39, 52) }, With(TrayIconStyle.DualBar));
        Assert.Null(m.Left);
        Assert.NotNull(m.Right);
    }

    [Fact]
    public void CustomThresholds_ChangeTheColour()
    {
        var settings = With(TrayIconStyle.Ring) with { Thresholds = new QuotaThresholds(50, 70) };
        var m = TrayIconModelBuilder.Build(new[] { Snap("claude", 55, 10) }, settings);
        Assert.Equal(Palette.Caution, m.Primary!.Colour);
    }
}
