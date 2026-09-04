using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class BurnRateFormattingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static BurnRateEstimate Estimate(
        double perHour, TimeSpan toFull, bool resetsFirst = false) =>
        new(perHour, toFull, Now + toFull, resetsFirst, 5, TimeSpan.FromHours(1));

    [Theory]
    [InlineData(90, "1h 30m")]
    [InlineData(45, "45m")]
    [InlineData(30, "30m")]
    [InlineData(2880, "2d 0h")]
    [InlineData(1500, "1d 1h")]
    public void Duration_English(int minutes, string expected) =>
        Assert.Equal(expected,
            QuotaFormatter.Duration(TimeSpan.FromMinutes(minutes), Strings.English));

    [Theory]
    [InlineData(90, "1s 30d")]
    [InlineData(45, "45d")]
    [InlineData(1500, "1g 1s")]
    public void Duration_Turkish(int minutes, string expected) =>
        Assert.Equal(expected,
            QuotaFormatter.Duration(TimeSpan.FromMinutes(minutes), Strings.Turkish));

    [Fact]
    public void Duration_UnderAMinute_SaysSoRatherThanZero()
    {
        Assert.Equal("under a minute",
            QuotaFormatter.Duration(TimeSpan.FromSeconds(20), Strings.English));
        Assert.Equal("bir dakikadan az",
            QuotaFormatter.Duration(TimeSpan.Zero, Strings.Turkish));
    }

    [Fact]
    public void PrecisePercent_KeepsADecimalForSmallRates()
    {
        Assert.Equal("0.7%", QuotaFormatter.PrecisePercent(0.7, Strings.English));
        Assert.Equal("%0.7", QuotaFormatter.PrecisePercent(0.7, Strings.Turkish));
    }

    [Fact]
    public void PrecisePercent_RoundsLargeRates()
    {
        Assert.Equal("13%", QuotaFormatter.PrecisePercent(12.6, Strings.English));
        Assert.Equal("%13", QuotaFormatter.PrecisePercent(12.6, Strings.Turkish));
    }

    [Fact]
    public void Pace_ReadsAsARate()
    {
        Assert.Equal("12% per hour",
            QuotaFormatter.Pace(Estimate(12, TimeSpan.FromHours(3)), Strings.English));
        Assert.Equal("saatte %12",
            QuotaFormatter.Pace(Estimate(12, TimeSpan.FromHours(3)), Strings.Turkish));
    }

    [Fact]
    public void Projection_NamesTheTimeRemaining()
    {
        Assert.Equal("full in ~2h 10m",
            QuotaFormatter.Projection(
                Estimate(12, TimeSpan.FromMinutes(130)), Strings.English));
        Assert.Equal("~2s 10d sonra dolar",
            QuotaFormatter.Projection(
                Estimate(12, TimeSpan.FromMinutes(130)), Strings.Turkish));
    }

    [Fact]
    public void Projection_WhenTheWindowResetsFirst_DoesNotShowACountdown()
    {
        // The countdown would be a true number telling a false story: at this rate the
        // window never actually fills, because it resets before it can.
        var estimate = Estimate(12, TimeSpan.FromHours(9), resetsFirst: true);

        Assert.Equal("resets before it fills",
            QuotaFormatter.Projection(estimate, Strings.English));
        Assert.Equal("dolmadan sıfırlanır",
            QuotaFormatter.Projection(estimate, Strings.Turkish));
        Assert.DoesNotContain("9", QuotaFormatter.Projection(estimate, Strings.English));
    }

    [Fact]
    public void BurnRate_PutsTheRateBeforeWhatItImplies()
    {
        Assert.Equal("12% per hour · full in ~3h 0m",
            QuotaFormatter.BurnRate(Estimate(12, TimeSpan.FromHours(3)), Strings.English));
    }

    [Fact]
    public void ProviderAndWindowTitles_AreTranslated()
    {
        Assert.Equal("Claude Usage", QuotaFormatter.ProviderTitle("claude", Strings.English));
        Assert.Equal("Codex Kullanımı", QuotaFormatter.ProviderTitle("codex", Strings.Turkish));
        Assert.Equal("mystery", QuotaFormatter.ProviderTitle("mystery", Strings.English));

        Assert.Equal("Weekly", QuotaFormatter.WindowTitle(WindowKind.Weekly, Strings.English));
        Assert.Equal("7 günlük pencere",
            QuotaFormatter.WindowSubtitle(WindowKind.Weekly, Strings.Turkish));
    }
}
