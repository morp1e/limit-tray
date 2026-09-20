using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class SettingsFileTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var original = AppSettings.Default with
        {
            Theme = ThemeMode.Light, Language = LanguageMode.English,
            RefreshSeconds = 300, Thresholds = new QuotaThresholds(70, 90), Notifications = false,
            TrayStyle = TrayIconStyle.Number, TraySource = TrayIconSource.CodexWeekly,
            ExpandedProviders = new HashSet<string> { "claude" },
        };

        var restored = SettingsFile.Read(SettingsFile.Write(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Read_MissingFieldsFallBackToDefaults()
    {
        var restored = SettingsFile.Read("""{"version":1,"theme":"dark"}""");
        Assert.NotNull(restored);
        Assert.Equal(ThemeMode.Dark, restored!.Theme);
        Assert.Equal(AppSettings.Default.RefreshSeconds, restored.RefreshSeconds);
        Assert.Equal(AppSettings.Default.TrayStyle, restored.TrayStyle);
    }

    [Fact]
    public void Read_UnknownFieldIsIgnored() =>
        Assert.NotNull(SettingsFile.Read("""{"version":1,"futureThing":true}"""));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"version":99}""")]
    public void Read_UnusableDocumentIsNull(string json) => Assert.Null(SettingsFile.Read(json));

    [Fact]
    public void Read_InvalidThresholdsAreRepaired()
    {
        var restored = SettingsFile.Read("""{"version":1,"cautionPercent":90,"warningPercent":80}""");
        Assert.Equal(QuotaThresholds.Default, restored!.Thresholds);
    }

    [Fact]
    public void Read_UnknownEnumValueFallsBackToDefault()
    {
        var restored = SettingsFile.Read("""{"version":1,"trayStyle":"hologram"}""");
        Assert.Equal(AppSettings.Default.TrayStyle, restored!.TrayStyle);
    }

    [Fact]
    public void Write_ContainsOnlySettingKeys()
    {
        // The file sits on disk. Nothing but settings goes in: no token, no account,
        // no snapshot detail, nothing derived from either.
        var json = SettingsFile.Write(AppSettings.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        var allowed = new HashSet<string>
        {
            "version", "theme", "language", "refreshSeconds",
            "cautionPercent", "warningPercent", "notifications", "trayStyle", "traySource",
            "expandedProviders",
        };
        // keys must be inside allowed; Assert.Subset reads the other way round and let
        // an extra key through. Spelled out so the direction cannot be misread again.
        Assert.True(keys.IsSubsetOf(allowed), "unexpected keys: " + string.Join(",", keys.Except(allowed)));
    }
}
