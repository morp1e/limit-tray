namespace LimitTray.Core.Settings;

public enum ThemeMode { System, Dark, Light }
public enum LanguageMode { System, Turkish, English }
public enum TrayIconStyle { Ring, Number, DualBar }
public enum TrayIconSource { Highest, ClaudeSession, ClaudeWeekly, CodexSession, CodexWeekly }

/// <summary>
/// Where the colour changes. Caution is a multiple of 5 between 50 and 80; warning sits
/// at least 5 above it and at most 95. The UI never offers anything else, and the file
/// reader repairs anything else to the defaults.
/// </summary>
public sealed record QuotaThresholds(double Caution, double Warning)
{
    public static readonly QuotaThresholds Default = new(60, 85);

    public bool IsValid =>
        Caution >= 50 && Caution <= 80 && Caution % 5 == 0
        && Warning >= Caution + 5 && Warning <= 95;
}

public sealed record AppSettings(
    ThemeMode Theme,
    LanguageMode Language,
    int RefreshSeconds,
    QuotaThresholds Thresholds,
    bool Notifications,
    TrayIconStyle TrayStyle,
    TrayIconSource TraySource,
    IReadOnlySet<string> ExpandedProviders)
{
    public static readonly int[] AllowedRefreshSeconds = { 60, 120, 300 };

    public static readonly AppSettings Default = new(
        ThemeMode.System, LanguageMode.System, RefreshSeconds: 120,
        QuotaThresholds.Default, Notifications: true, TrayIconStyle.DualBar,
        TrayIconSource.Highest, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Returns a copy with every out-of-range value replaced by its default.</summary>
    public AppSettings Normalised() => this with
    {
        RefreshSeconds = Array.IndexOf(AllowedRefreshSeconds, RefreshSeconds) >= 0
            ? RefreshSeconds : Default.RefreshSeconds,
        Thresholds = Thresholds.IsValid ? Thresholds : QuotaThresholds.Default,
    };

    public bool Equals(AppSettings? other) =>
        other is not null
        && Theme == other.Theme && Language == other.Language
        && RefreshSeconds == other.RefreshSeconds
        && Thresholds == other.Thresholds && Notifications == other.Notifications
        && TrayStyle == other.TrayStyle && TraySource == other.TraySource
        && ExpandedProviders.SetEquals(other.ExpandedProviders);

    public override int GetHashCode() => HashCode.Combine(
        Theme, Language, RefreshSeconds, Thresholds, Notifications, TrayStyle, TraySource);
}
