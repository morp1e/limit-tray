using System.Globalization;

namespace LimitTray.Core.Presentation;

/// <summary>Contains the application's user-facing text for one language.</summary>
public sealed class Strings
{
    public static readonly Strings English = new(
        exit: "Exit",
        session: "Session",
        weekly: "Weekly",
        sessionWindow: "5-hour window",
        weeklyWindow: "7-day window",
        noData: "No data yet",
        claudeUsage: "Claude Usage",
        codexUsage: "Codex Usage",
        resetUnknown: "Reset time unknown",
        resetting: "Resetting",
        resetSoon: "resets soon",
        resetHoursMinutes: "{0}h {1}m until reset",
        resetMinutes: "{0}m until reset",
        resetDaysHours: "{0}d {1}h until reset",
        updatedNow: "Updated just now",
        ageMinutes: "{0}m ago",
        ageHours: "{0}h ago",
        percentFormat: "{0}%",
        rateLimited: "Usage check throttled, not your quota",
        authMissing: "Login required",
        protocolBroken: "API changed",
        stale: "Data is stale");

    public static readonly Strings Turkish = new(
        exit: "Çıkış",
        session: "Oturum",
        weekly: "Haftalık",
        sessionWindow: "5 saatlik pencere",
        weeklyWindow: "7 günlük pencere",
        noData: "Henüz veri yok",
        claudeUsage: "Claude Kullanımı",
        codexUsage: "Codex Kullanımı",
        resetUnknown: "Sıfırlanma zamanı bilinmiyor",
        resetting: "Sıfırlanıyor",
        resetSoon: "birazdan sıfırlanır",
        resetHoursMinutes: "{0}s {1}d sonra sıfırlanır",
        resetMinutes: "{0}d sonra sıfırlanır",
        resetDaysHours: "{0}g {1}s sonra sıfırlanır",
        updatedNow: "Şimdi güncellendi",
        ageMinutes: "{0} dk önce",
        ageHours: "{0} sa önce",
        percentFormat: "%{0}",
        rateLimited: "Sorgu sınırlandı, kotan dolmadı",
        authMissing: "Giriş gerekli",
        protocolBroken: "API değişmiş",
        stale: "Veri eski");

    private Strings(
        string exit, string session, string weekly, string sessionWindow, string weeklyWindow,
        string noData, string claudeUsage, string codexUsage, string resetUnknown,
        string resetting, string resetSoon, string resetHoursMinutes, string resetMinutes, string resetDaysHours,
        string updatedNow, string ageMinutes, string ageHours, string percentFormat, string rateLimited,
        string authMissing, string protocolBroken, string stale)
    {
        Exit = exit;
        Session = session;
        Weekly = weekly;
        SessionWindow = sessionWindow;
        WeeklyWindow = weeklyWindow;
        NoData = noData;
        ClaudeUsage = claudeUsage;
        CodexUsage = codexUsage;
        ResetUnknown = resetUnknown;
        Resetting = resetting;
        ResetSoon = resetSoon;
        ResetHoursMinutes = resetHoursMinutes;
        ResetMinutes = resetMinutes;
        ResetDaysHours = resetDaysHours;
        UpdatedNow = updatedNow;
        AgeMinutes = ageMinutes;
        AgeHours = ageHours;
        PercentFormat = percentFormat;
        RateLimited = rateLimited;
        AuthMissing = authMissing;
        ProtocolBroken = protocolBroken;
        Stale = stale;
    }

    public string Exit { get; }
    public string Session { get; }
    public string Weekly { get; }
    public string SessionWindow { get; }
    public string WeeklyWindow { get; }
    public string NoData { get; }
    public string ClaudeUsage { get; }
    public string CodexUsage { get; }
    public string ResetUnknown { get; }
    public string Resetting { get; }
    public string ResetSoon { get; }
    public string ResetHoursMinutes { get; }
    public string ResetMinutes { get; }
    public string ResetDaysHours { get; }
    public string UpdatedNow { get; }
    public string AgeMinutes { get; }
    public string AgeHours { get; }
    public string PercentFormat { get; }
    public string RateLimited { get; }
    public string AuthMissing { get; }
    public string ProtocolBroken { get; }
    public string Stale { get; }

    public static Strings ForCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? Turkish
            : English;
}
