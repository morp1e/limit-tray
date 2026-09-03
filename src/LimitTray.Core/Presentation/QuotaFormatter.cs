using System.Globalization;
using LimitTray.Core.Model;

namespace LimitTray.Core.Presentation;

public static class QuotaFormatter
{
    public const double CautionThreshold = 60.0;
    public const double WarningThreshold = 85.0;

    public static QuotaSeverity SeverityFor(double percent) => percent switch
    {
        > WarningThreshold => QuotaSeverity.Warning,
        >= CautionThreshold => QuotaSeverity.Caution,
        _ => QuotaSeverity.Normal,
    };

    public static string Percent(double value) =>
        "%" + Math.Round(value, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    public static string ResetsIn(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "sifirlanma zamani bilinmiyor";

        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return "sifirlaniyor";
        if (remaining < TimeSpan.FromMinutes(1)) return "birazdan sifirlanir";

        if (remaining >= TimeSpan.FromDays(1))
            return $"{remaining.Days}g {remaining.Hours}s sonra sifirlanir";

        if (remaining >= TimeSpan.FromHours(1))
            return $"{remaining.Hours}s {remaining.Minutes}d sonra sifirlanir";

        return $"{remaining.Minutes}d sonra sifirlanir";
    }

    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var age = now - fetchedAt;
        if (age < TimeSpan.FromMinutes(1)) return "simdi guncellendi";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} dk once";
        return $"{(int)age.TotalHours} sa once";
    }

    public static string HealthText(QuotaSnapshot snapshot) => snapshot.Health switch
    {
        HealthState.RateLimited => "Gecici olarak sinirli",
        HealthState.AuthMissing => "Giris gerekli",
        HealthState.ProtocolBroken => "API degismis",
        HealthState.Stale => "Veri eski",
        _ => "",
    };

    public static string ShortName(string provider) => provider switch
    {
        "claude" => "CC",
        "codex" => "CX",
        _ => provider.ToUpperInvariant(),
    };

    public static string Tooltip(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        var parts = new List<string>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var name = ShortName(snapshot.Provider);

            if (snapshot.Session is null && snapshot.Weekly is null)
            {
                parts.Add($"{name} {HealthText(snapshot)}");
                continue;
            }

            var session = snapshot.Session is null ? "?" : Percent(snapshot.Session.Percent);
            var weekly = snapshot.Weekly is null ? "?" : Percent(snapshot.Weekly.Percent);
            parts.Add($"{name} {session} / {weekly}");
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Herhangi bir saglayicinin verisi alinamiyorsa true. Stale sayilmaz:
    /// eski veri hala gercek veridir, alinamayan veri degildir.
    /// </summary>
    public static bool HasUnhealthy(IReadOnlyList<QuotaSnapshot> snapshots) =>
        snapshots.Any(s => s.Health is HealthState.RateLimited
            or HealthState.AuthMissing or HealthState.ProtocolBroken);

    /// <summary>
    /// Simgede gosterilecek deger: saglikli snapshot'lardaki en yuksek yuzde.
    /// Sagliksiz snapshot hic sayilmaz — hata sifir gibi gorunemez.
    /// </summary>
    public static double? HighestPercent(IReadOnlyList<QuotaSnapshot> snapshots)
    {
        double? highest = null;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Health is HealthState.RateLimited or HealthState.AuthMissing
                or HealthState.ProtocolBroken)
                continue;

            foreach (var window in new[] { snapshot.Session, snapshot.Weekly })
            {
                if (window is null) continue;
                if (highest is null || window.Percent > highest) highest = window.Percent;
            }
        }

        return highest;
    }
}
