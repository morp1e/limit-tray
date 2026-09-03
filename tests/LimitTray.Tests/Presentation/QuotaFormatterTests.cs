using System.Globalization;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class QuotaFormatterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private static readonly Strings Tr = Strings.Turkish;
    private static readonly Strings En = Strings.English;

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
    [InlineData(79.0, "79")]
    [InlineData(14.0, "14")]
    [InlineData(0.0, "0")]
    [InlineData(36.4, "36")]
    [InlineData(99.5, "100")]
    public void Percent_RoundsToWholeNumberInBothLanguages(double value, string rounded)
    {
        Assert.Equal(string.Format(Tr.PercentFormat, rounded), QuotaFormatter.Percent(value, Tr));
        Assert.Equal(string.Format(En.PercentFormat, rounded), QuotaFormatter.Percent(value, En));
    }

    [Fact]
    public void ResetsIn_HoursAndMinutes() =>
        Assert.Equal("4s 38d sonra sıfırlanır",
            QuotaFormatter.ResetsIn(Now.AddMinutes(278), Now, Tr));

    [Fact]
    public void ResetsIn_DaysAndHours() =>
        Assert.Equal("1g 14s sonra sıfırlanır",
            QuotaFormatter.ResetsIn(Now.AddHours(38), Now, Tr));

    [Fact]
    public void ResetsIn_UnderOneMinute() =>
        Assert.Equal("birazdan sıfırlanır",
            QuotaFormatter.ResetsIn(Now.AddSeconds(30), Now, Tr));

    [Fact]
    public void ResetsIn_PastTime_SaysResetting() =>
        Assert.Equal("Sıfırlanıyor", QuotaFormatter.ResetsIn(Now.AddMinutes(-5), Now, Tr));

    [Fact]
    public void ResetsIn_Null_SaysUnknown() =>
        Assert.Equal("Sıfırlanma zamanı bilinmiyor", QuotaFormatter.ResetsIn(null, Now, Tr));

    [Fact]
    public void Age_JustNow() =>
        Assert.Equal("Şimdi güncellendi", QuotaFormatter.Age(Now.AddSeconds(-10), Now, Tr));

    [Fact]
    public void Age_Minutes() =>
        Assert.Equal("3 dk önce", QuotaFormatter.Age(Now.AddMinutes(-3), Now, Tr));

    [Theory]
    [InlineData(HealthState.RateLimited, "Sorgu sınırlandı, kotan dolmadı")]
    [InlineData(HealthState.AuthMissing, "Giriş gerekli")]
    [InlineData(HealthState.ProtocolBroken, "API değişmiş")]
    [InlineData(HealthState.Stale, "Veri eski")]
    public void HealthText_DescribesFailure(HealthState health, string expected)
    {
        var snapshot = QuotaSnapshot.Unhealthy("claude", health, Now, "detay");
        Assert.Equal(expected, QuotaFormatter.HealthText(snapshot, Tr));
    }

    [Theory]
    [InlineData(HealthState.RateLimited, "Usage check throttled, not your quota")]
    [InlineData(HealthState.AuthMissing, "Login required")]
    [InlineData(HealthState.ProtocolBroken, "API changed")]
    [InlineData(HealthState.Stale, "Data is stale")]
    public void HealthText_DescribesFailureInEnglish(HealthState health, string expected)
    {
        var snapshot = QuotaSnapshot.Unhealthy("claude", health, Now, "detail");
        Assert.Equal(expected, QuotaFormatter.HealthText(snapshot, En));
    }

    [Fact]
    public void EnglishFormatting_CoversResetsAndAge()
    {
        Assert.Equal("4h 38m until reset", QuotaFormatter.ResetsIn(Now.AddMinutes(278), Now, En));
        Assert.Equal("3m ago", QuotaFormatter.Age(Now.AddMinutes(-3), Now, En));
    }

    [Fact]
    public void CultureSelection_UsesTurkishOnlyForTurkishCulture()
    {
        Assert.Same(Strings.Turkish, Strings.ForCulture(CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Same(Strings.English, Strings.ForCulture(CultureInfo.GetCultureInfo("en-US")));
        Assert.Same(Strings.English, Strings.ForCulture(CultureInfo.GetCultureInfo("de-DE")));
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

        Assert.Equal($"CC {QuotaFormatter.Percent(14, Tr)} / {QuotaFormatter.Percent(12, Tr)}  ·  "
                     + $"CX {QuotaFormatter.Percent(0, Tr)} / {QuotaFormatter.Percent(36, Tr)}",
            QuotaFormatter.Tooltip(snapshots, Now, Tr));
    }

    [Fact]
    public void Tooltip_UnhealthyProviderShowsReasonNotZero()
    {
        var snapshots = new[]
        {
            QuotaSnapshot.Unhealthy("claude", HealthState.AuthMissing, Now, "detay"),
        };

        var tooltip = QuotaFormatter.Tooltip(snapshots, Now, Tr);

        Assert.Contains("Giriş gerekli", tooltip);
        Assert.DoesNotContain(QuotaFormatter.Percent(0, Tr), tooltip);
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
