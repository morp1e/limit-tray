using System.Globalization;
using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Core.Presentation;

public static class QuotaFormatter
{
    /// <summary>Separator between two facts on one line.</summary>
    public const string Separator = " · ";

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

    public static string Percent(double value) => Percent(value, DefaultStrings);

    public static string Percent(double value, Strings strings) =>
        string.Format(CultureInfo.InvariantCulture, strings.PercentFormat,
            Math.Round(value, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture));

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

    /// <summary>
    /// A percentage that keeps one decimal below 10, so a slow burn rate does not get
    /// rounded up into a number that overstates it.
    /// </summary>
    public static string PrecisePercent(double value, Strings strings)
    {
        var rounded = Math.Abs(value) < 10.0
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(value, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);

        return string.Format(CultureInfo.InvariantCulture, strings.PercentFormat, rounded);
    }

    /// <summary>A bare duration such as "2h 10m", with no "until reset" framing.</summary>
    public static string Duration(TimeSpan value, Strings strings)
    {
        if (value < TimeSpan.FromMinutes(1)) return strings.DurationUnderMinute;

        if (value >= TimeSpan.FromDays(1))
            return string.Format(CultureInfo.InvariantCulture, strings.DurationDaysHours,
                value.Days, value.Hours);

        if (value >= TimeSpan.FromHours(1))
            return string.Format(CultureInfo.InvariantCulture, strings.DurationHoursMinutes,
                value.Hours, value.Minutes);

        return string.Format(CultureInfo.InvariantCulture, strings.DurationMinutes, value.Minutes);
    }

    /// <summary>The measured consumption rate, e.g. "12% per hour".</summary>
    public static string Pace(BurnRateEstimate estimate, Strings strings) =>
        string.Format(CultureInfo.InvariantCulture, strings.Pace,
            PrecisePercent(estimate.PercentPerHour, strings));

    /// <summary>
    /// What the rate implies. When the window resets before the projection lands, the
    /// projection is not shown at all: a countdown to an exhaustion that cannot happen
    /// would be a true number telling a false story.
    /// </summary>
    public static string Projection(BurnRateEstimate estimate, Strings strings) =>
        estimate.ResetsFirst
            ? strings.ResetsBeforeFull
            : string.Format(CultureInfo.InvariantCulture, strings.FullIn,
                Duration(estimate.TimeToFull, strings));

    /// <summary>The full burn-rate line: rate first, then what it implies.</summary>
    public static string BurnRate(BurnRateEstimate estimate, Strings strings) =>
        Pace(estimate, strings) + Separator + Projection(estimate, strings);

    public static string ProviderTitle(string provider, Strings strings) => provider switch
    {
        "claude" => strings.ClaudeUsage,
        "codex" => strings.CodexUsage,
        _ => provider,
    };

    public static string WindowTitle(WindowKind kind, Strings strings) =>
        kind == WindowKind.Session ? strings.Session : strings.Weekly;

    public static string WindowSubtitle(WindowKind kind, Strings strings) =>
        kind == WindowKind.Session ? strings.SessionWindow : strings.WeeklyWindow;

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

            var session = snapshot.Session is null ? "?" : Percent(snapshot.Session.Percent, strings);
            var weekly = snapshot.Weekly is null ? "?" : Percent(snapshot.Weekly.Percent, strings);
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
