using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Default_MatchesSpec()
    {
        var s = AppSettings.Default;
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(LanguageMode.System, s.Language);
        Assert.Equal(120, s.RefreshSeconds);
        Assert.Equal(60, s.Thresholds.Caution);
        Assert.Equal(85, s.Thresholds.Warning);
        Assert.True(s.Notifications);
        Assert.Equal(TrayIconStyle.DualBar, s.TrayStyle);
        Assert.Equal(TrayIconSource.Highest, s.TraySource);
        Assert.Empty(s.ExpandedProviders);
    }

    [Theory]
    [InlineData(60, 85, true)]
    [InlineData(50, 55, true)]
    [InlineData(80, 95, true)]
    [InlineData(45, 85, false)]   // caution below 50
    [InlineData(85, 90, false)]   // caution above 80
    [InlineData(62, 85, false)]   // not a multiple of 5
    [InlineData(60, 60, false)]   // warning must be caution + 5 or more
    [InlineData(60, 100, false)]  // warning above 95
    public void Thresholds_IsValid(double caution, double warning, bool expected) =>
        Assert.Equal(expected, new QuotaThresholds(caution, warning).IsValid);

    [Fact]
    public void Normalised_RepairsInvalidValuesToDefaults()
    {
        var broken = AppSettings.Default with
        {
            RefreshSeconds = 7,
            Thresholds = new QuotaThresholds(90, 80),
        };

        var fixedUp = broken.Normalised();

        Assert.Equal(120, fixedUp.RefreshSeconds);
        Assert.Equal(QuotaThresholds.Default, fixedUp.Thresholds);
    }

    [Fact]
    public void Normalised_KeepsValidValues()
    {
        var s = AppSettings.Default with { RefreshSeconds = 300, Thresholds = new QuotaThresholds(70, 90) };
        Assert.Equal(s, s.Normalised());
    }
}
