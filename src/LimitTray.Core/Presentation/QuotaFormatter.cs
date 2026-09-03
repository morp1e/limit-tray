using System.Globalization;
using LimitTray.Core.Model;

namespace LimitTray.Core.Presentation;

public static class QuotaFormatter
{
    public static Strings DefaultStrings { get; set; } =
        Strings.ForCulture(CultureInfo.CurrentUICulture);
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

    public static string ResetsIn(DateTimeOffset? resetsAt, DateTimeOffset now) =>
        ResetsIn(resetsAt, now, DefaultStrings);

    public static string ResetsIn(DateTimeOffset? resetsAt, DateTimeOffset now, Strings strings)
    {
        if (resetsAt is null) return strings.ResetUnknown;

        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return strings.Resetting;
        if (remaining < TimeSpan.FromMinutes(1)) return strings.ResetSoon;

        if (remaining >= TimeSpan.FromDays(1))
            return string.Format(CultureInfo.InvariantCulture, strings.ResetDaysHours,
                remaining.Days, remaining.Hours);

        if (remaining >= TimeSpan.FromHours(1))
            return string.Format(CultureInfo.InvariantCulture, strings.ResetHoursMinutes,
                remaining.Hours, remaining.Minutes);

        return string.Format(CultureInfo.InvariantCulture, strings.ResetMinutes, remaining.Minutes);
    }

    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now) =>
        Age(fetchedAt, now, DefaultStrings);

    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now, Strings strings)
    {
        var age = now - fetchedAt;
        if (age < TimeSpan.FromMinutes(1)) return strings.UpdatedNow;
        if (age < TimeSpan.FromHours(1))
            return string.Format(CultureInfo.InvariantCulture, strings.AgeMinutes, (int)age.TotalMinutes);
        return string.Format(CultureInfo.InvariantCulture, strings.AgeHours, (int)age.TotalHours);
    }

    public static string HealthText(QuotaSnapshot snapshot) => HealthText(snapshot, DefaultStrings);

    public static string HealthText(QuotaSnapshot snapshot, Strings strings) => snapshot.Health switch
    {
        HealthState.RateLimited => strings.RateLimited,
        HealthState.AuthMissing => strings.AuthMissing,
        HealthState.ProtocolBroken => strings.ProtocolBroken,
        HealthState.Stale => strings.Stale,
        _ => "",
    };

    public static string ShortName(string provider) => provider switch
    {
        "claude" => "CC",
        "codex" => "CX",
        _ => provider.ToUpperInvariant(),
    };

    public static string Tooltip(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now) =>
        Tooltip(snapshots, now, DefaultStrings);

    public static string Tooltip(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now, Strings strings)
    {
        var parts = new List<string>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var name = ShortName(snapshot.Provider);

            if (snapshot.Session is null && snapshot.Weekly is null)
            {
                parts.Add($"{name} {HealthText(snapshot, strings)}");
                continue;
            }

            var session = snapshot.Session is null ? "?" : Percent(snapshot.Session.Percent);
            var weekly = snapshot.Weekly is null ? "?" : Percent(snapshot.Weekly.Percent);
            parts.Add($"{name} {session} / {weekly}");
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// True when data cannot be obtained from any provider. Stale does not count:
    /// old data is still real data, not unavailable data.
    /// </summary>
    public static bool HasUnhealthy(IReadOnlyList<QuotaSnapshot> snapshots) =>
        snapshots.Any(s => s.Health is HealthState.RateLimited
            or HealthState.AuthMissing or HealthState.ProtocolBroken);

    /// <summary>
    /// The value shown in the icon: the highest percentage among healthy snapshots.
    /// Unhealthy snapshots are excluded entirely — an error must not look like zero.
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
