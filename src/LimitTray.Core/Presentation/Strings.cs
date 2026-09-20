using System.Globalization;

namespace LimitTray.Core.Presentation;

/// <summary>
/// The few symbols the panel draws as text. They are language-neutral, but they are
/// non-ASCII, and this file is the one place source may carry that.
/// </summary>
public static class Glyphs
{
    public const string Refresh = "↻";   // clockwise open circle arrow
    public const string Settings = "⚙";  // gear
    public const string Back = "‹";      // single left-pointing angle quotation mark
    public const string Terminal = "⌁";  // electric arrow
    public const string Unknown = "?";
}

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
        Refresh = "Refresh",
        Settings = "Settings",
        BackToPanel = "Back to panel",
        OpenTerminal = "Open in terminal",
        TerminalFailed = "Could not open a terminal",
        StaleBadge = "STALE · {0}",
        LastUpdated = "Last updated {0}",
        GroupAppearance = "Appearance",
        GroupData = "Data",
        GroupTray = "Tray icon",
        GroupSystem = "System",
        Theme = "Theme",
        ThemeSystem = "System",
        ThemeDark = "Dark",
        ThemeLight = "Light",
        Language = "Language",
        LanguageSystem = "System",
        RefreshInterval = "Refresh interval",
        Seconds = "{0} s",
        Minutes = "{0} min",
        CautionThreshold = "Caution threshold",
        WarningThreshold = "Warning threshold",
        Notifications = "Notifications",
        TrayStyle = "Icon style",
        TrayStyleRing = "Ring",
        TrayStyleNumber = "Number",
        TrayStyleDualBar = "Two bars",
        TraySource = "Icon source",
        TraySourceHighest = "Fullest window",
        TraySourceClaudeSession = "Claude 5h",
        TraySourceClaudeWeekly = "Claude 7d",
        TraySourceCodexSession = "Codex 5h",
        TraySourceCodexWeekly = "Codex 7d",
        SettingsSaveFailed = "Settings could not be saved",
        ResetShort = "↺ {0}",
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
        Refresh = "Yenile",
        Settings = "Ayarlar",
        BackToPanel = "Panele dön",
        OpenTerminal = "Terminalde aç",
        TerminalFailed = "Terminal açılamadı",
        StaleBadge = "ESKİ · {0}",
        LastUpdated = "Son güncelleme {0}",
        GroupAppearance = "Görünüm",
        GroupData = "Veri",
        GroupTray = "Tray ikonu",
        GroupSystem = "Sistem",
        Theme = "Tema",
        ThemeSystem = "Sistem",
        ThemeDark = "Koyu",
        ThemeLight = "Açık",
        Language = "Dil",
        LanguageSystem = "Sistem",
        RefreshInterval = "Yenileme aralığı",
        Seconds = "{0} sn",
        Minutes = "{0} dk",
        CautionThreshold = "Dikkat eşiği",
        WarningThreshold = "Uyarı eşiği",
        Notifications = "Bildirim",
        TrayStyle = "İkon stili",
        TrayStyleRing = "Halka",
        TrayStyleNumber = "Sayı",
        TrayStyleDualBar = "İki bar",
        TraySource = "İkon kaynağı",
        TraySourceHighest = "En dolu pencere",
        TraySourceClaudeSession = "Claude 5s",
        TraySourceClaudeWeekly = "Claude 7g",
        TraySourceCodexSession = "Codex 5s",
        TraySourceCodexWeekly = "Codex 7g",
        SettingsSaveFailed = "Ayarlar kaydedilemedi",
        ResetShort = "↺ {0}",
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

    public required string Refresh { get; init; }
    public required string Settings { get; init; }
    public required string BackToPanel { get; init; }
    public required string OpenTerminal { get; init; }
    public required string TerminalFailed { get; init; }
    public required string StaleBadge { get; init; }
    public required string LastUpdated { get; init; }
    public required string GroupAppearance { get; init; }
    public required string GroupData { get; init; }
    public required string GroupTray { get; init; }
    public required string GroupSystem { get; init; }
    public required string Theme { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeDark { get; init; }
    public required string ThemeLight { get; init; }
    public required string Language { get; init; }
    public required string LanguageSystem { get; init; }
    public required string RefreshInterval { get; init; }
    public required string Seconds { get; init; }
    public required string Minutes { get; init; }
    public required string CautionThreshold { get; init; }
    public required string WarningThreshold { get; init; }
    public required string Notifications { get; init; }
    public required string TrayStyle { get; init; }
    public required string TrayStyleRing { get; init; }
    public required string TrayStyleNumber { get; init; }
    public required string TrayStyleDualBar { get; init; }
    public required string TraySource { get; init; }
    public required string TraySourceHighest { get; init; }
    public required string TraySourceClaudeSession { get; init; }
    public required string TraySourceClaudeWeekly { get; init; }
    public required string TraySourceCodexSession { get; init; }
    public required string TraySourceCodexWeekly { get; init; }
    public required string SettingsSaveFailed { get; init; }
    public required string ResetShort { get; init; }

    public static Strings ForCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? Turkish
            : English;
}
