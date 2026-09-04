using System.Globalization;

namespace LimitTray.Core.Presentation;

/// <summary>
/// The application's user-facing text for one language. Every displayed string lives
/// here, including the tray menu and the balloon notifications.
///
/// The members are required init properties rather than constructor parameters: with
/// this many strings a positional constructor makes it easy to shift two neighbouring
/// values by one and produce a build that compiles and lies.
/// </summary>
public sealed class Strings
{
    private Strings() { }

    public static readonly Strings English = new()
    {
        Exit = "Exit",
        StartWithWindows = "Start with Windows",
        Session = "Session",
        Weekly = "Weekly",
        SessionWindow = "5-hour window",
        WeeklyWindow = "7-day window",
        NoData = "No data yet",
        ClaudeUsage = "Claude Usage",
        CodexUsage = "Codex Usage",
        ResetUnknown = "Reset time unknown",
        Resetting = "Resetting",
        ResetSoon = "resets soon",
        ResetHoursMinutes = "{0}h {1}m until reset",
        ResetMinutes = "{0}m until reset",
        ResetDaysHours = "{0}d {1}h until reset",
        UpdatedNow = "Updated just now",
        AgeMinutes = "{0}m ago",
        AgeHours = "{0}h ago",
        PercentFormat = "{0}%",
        RateLimited = "Usage check throttled, not your quota",
        AuthMissing = "Login required",
        ProtocolBroken = "API changed",
        Stale = "Data is stale",
        DurationDaysHours = "{0}d {1}h",
        DurationHoursMinutes = "{0}h {1}m",
        DurationMinutes = "{0}m",
        DurationUnderMinute = "under a minute",
        Pace = "{0} per hour",
        FullIn = "full in ~{0}",
        ResetsBeforeFull = "resets before it fills",
        WarningNotificationTitle = "Lim'it - running low",
        WarningNotificationBody = "{0}, {1}: {2}",
    };

    public static readonly Strings Turkish = new()
    {
        Exit = "Çıkış",
        StartWithWindows = "Windows ile başlat",
        Session = "Oturum",
        Weekly = "Haftalık",
        SessionWindow = "5 saatlik pencere",
        WeeklyWindow = "7 günlük pencere",
        NoData = "Henüz veri yok",
        ClaudeUsage = "Claude Kullanımı",
        CodexUsage = "Codex Kullanımı",
        ResetUnknown = "Sıfırlanma zamanı bilinmiyor",
        Resetting = "Sıfırlanıyor",
        ResetSoon = "birazdan sıfırlanır",
        ResetHoursMinutes = "{0}s {1}d sonra sıfırlanır",
        ResetMinutes = "{0}d sonra sıfırlanır",
        ResetDaysHours = "{0}g {1}s sonra sıfırlanır",
        UpdatedNow = "Şimdi güncellendi",
        AgeMinutes = "{0} dk önce",
        AgeHours = "{0} sa önce",
        PercentFormat = "%{0}",
        RateLimited = "Sorgu sınırlandı, kotan dolmadı",
        AuthMissing = "Giriş gerekli",
        ProtocolBroken = "API değişmiş",
        Stale = "Veri eski",
        DurationDaysHours = "{0}g {1}s",
        DurationHoursMinutes = "{0}s {1}d",
        DurationMinutes = "{0}d",
        DurationUnderMinute = "bir dakikadan az",
        Pace = "saatte {0}",
        FullIn = "~{0} sonra dolar",
        ResetsBeforeFull = "dolmadan sıfırlanır",
        WarningNotificationTitle = "Lim'it - azalıyor",
        WarningNotificationBody = "{0}, {1}: {2}",
    };

    public required string Exit { get; init; }
    public required string StartWithWindows { get; init; }
    public required string Session { get; init; }
    public required string Weekly { get; init; }
    public required string SessionWindow { get; init; }
    public required string WeeklyWindow { get; init; }
    public required string NoData { get; init; }
    public required string ClaudeUsage { get; init; }
    public required string CodexUsage { get; init; }
    public required string ResetUnknown { get; init; }
    public required string Resetting { get; init; }
    public required string ResetSoon { get; init; }
    public required string ResetHoursMinutes { get; init; }
    public required string ResetMinutes { get; init; }
    public required string ResetDaysHours { get; init; }
    public required string UpdatedNow { get; init; }
    public required string AgeMinutes { get; init; }
    public required string AgeHours { get; init; }
    public required string PercentFormat { get; init; }
    public required string RateLimited { get; init; }
    public required string AuthMissing { get; init; }
    public required string ProtocolBroken { get; init; }
    public required string Stale { get; init; }

    /// <summary>Bare durations, used by the burn-rate projection.</summary>
    public required string DurationDaysHours { get; init; }
    public required string DurationHoursMinutes { get; init; }
    public required string DurationMinutes { get; init; }
    public required string DurationUnderMinute { get; init; }

    /// <summary>Consumption rate, e.g. "12% per hour".</summary>
    public required string Pace { get; init; }

    /// <summary>Projected exhaustion, e.g. "full in ~2h 10m".</summary>
    public required string FullIn { get; init; }

    /// <summary>Shown instead of a projection when the window resets before it fills.</summary>
    public required string ResetsBeforeFull { get; init; }

    public required string WarningNotificationTitle { get; init; }

    /// <summary>Provider title, window subtitle, percentage.</summary>
    public required string WarningNotificationBody { get; init; }

    public static Strings ForCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? Turkish
            : English;
}
