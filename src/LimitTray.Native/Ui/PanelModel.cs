using System.Globalization;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.Native.Ui;

/// <summary>One quota window inside a card; the v0.3 WindowRowViewModel without WPF types.</summary>
internal sealed record RowModel(
    WindowKind Kind,
    string Label,
    double Percent,
    string PercentText,
    Rgb Colour,
    byte Alpha,
    bool Glow,
    string? BurnRateText,
    IReadOnlyList<PointF01>? Sparkline);

/// <summary>A sparkline point with both coordinates in 0..1 (x: time, y: percent, 1 = top).</summary>
internal readonly record struct PointF01(float X, float Y);

/// <summary>One provider card; the v0.3 ProviderCardViewModel without WPF types.</summary>
internal sealed record CardModel(
    string Provider,
    string Title,
    double RingPercent,
    string RingPercentText,
    Rgb RingColour,
    byte Alpha,
    bool RingGlow,
    string ResetShortText,
    IReadOnlyList<RowModel> Rows,
    bool IsDimmed,
    string? HealthText,
    bool HasData,
    string LastUpdatedText);

internal static class PanelModel
{
    private const int MinimumSparklineSamples = 5;
    private const double MinimumSparklineRange = 1.0;

    public static CardModel Card(
        QuotaSnapshot snapshot, UsageHistory history, AppSettings settings, Strings strings, DateTimeOffset now)
    {
        var fresh = snapshot.Health == HealthState.Fresh;
        var alpha = Theme.OpacityFor(snapshot.Health);
        var windows = Windows(snapshot).ToList();
        (WindowKind Kind, QuotaWindow Window)? fullest = windows.Count == 0
            ? null
            : windows.MaxBy(pair => pair.Window.Percent);

        // An unknown value is the question mark, never an empty gauge that reads as 0%.
        var ringPercent = fullest is null ? 0 : Math.Clamp(fullest.Value.Window.Percent, 0, 100);
        var ringText = fullest is null ? Glyphs.Unknown : QuotaFormatter.Percent(fullest.Value.Window.Percent, strings);
        var ringColour = fullest is null
            ? Theme.ColourFor(snapshot.Provider, QuotaSeverity.Normal, snapshot.Health)
            : Theme.ColourFor(snapshot.Provider,
                QuotaFormatter.SeverityFor(fullest.Value.Window.Percent, settings.Thresholds), snapshot.Health);

        var rows = windows
            .Select(pair => Row(pair.Kind, pair.Window, snapshot, history, settings, strings, now))
            .ToList();

        // "Updated just now" already carries its own verb; wrapping it in "Last updated ..."
        // read as a stutter on screen.
        var age = QuotaFormatter.Age(snapshot.FetchedAt, now, strings);
        var lastUpdated = age == strings.UpdatedNow
            ? age
            : string.Format(CultureInfo.InvariantCulture, strings.LastUpdated, age);

        return new CardModel(
            snapshot.Provider,
            QuotaFormatter.ProviderTitle(snapshot.Provider, strings),
            ringPercent,
            ringText,
            ringColour,
            alpha,
            RingGlow: fresh,
            ResetShort(windows, now, strings),
            rows,
            IsDimmed: !fresh,
            HealthText: fresh ? null : QuotaFormatter.HealthText(snapshot, strings),
            HasData: snapshot.Session is not null || snapshot.Weekly is not null,
            lastUpdated);
    }

    public static string Footer(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now, Strings strings) =>
        snapshots.Count == 0
            ? strings.NoData
            : QuotaFormatter.Age(snapshots.Max(snapshot => snapshot.FetchedAt), now, strings);

    private static RowModel Row(
        WindowKind kind, QuotaWindow window, QuotaSnapshot snapshot, UsageHistory history,
        AppSettings settings, Strings strings, DateTimeOffset now)
    {
        var colour = Theme.ColourFor(
            snapshot.Provider, QuotaFormatter.SeverityFor(window.Percent, settings.Thresholds), snapshot.Health);
        var estimate = snapshot.Health == HealthState.Fresh
            ? history.Estimate(snapshot.Provider, kind, now)
            : null;

        return new RowModel(
            kind,
            QuotaFormatter.WindowTitle(kind, strings),
            Math.Clamp(window.Percent, 0, 100),
            QuotaFormatter.Percent(window.Percent, strings),
            colour,
            Theme.OpacityFor(snapshot.Health),
            // Stale and error rows are muted; a glow under grey would only be noise.
            Glow: snapshot.Health == HealthState.Fresh,
            estimate is null ? null : QuotaFormatter.BurnRate(estimate, strings),
            Sparkline(history.Samples(snapshot.Provider, kind)));
    }

    /// <summary>
    /// Normalised sparkline, or null when the data cannot draw an honest line. v0.3 laid the
    /// points out on a fixed 252 px width inside a 104 px clipped canvas, so only the oldest
    /// 41% of the series was ever visible; here the whole series is scaled to the box.
    /// </summary>
    internal static IReadOnlyList<PointF01>? Sparkline(IReadOnlyList<UsageSample> samples)
    {
        if (samples.Count < MinimumSparklineSamples) return null;

        var min = samples.Min(sample => sample.Percent);
        var max = samples.Max(sample => sample.Percent);
        if (max - min < MinimumSparklineRange) return null;

        var span = (samples[^1].At - samples[0].At).TotalSeconds;
        if (span <= 0) return null;

        var points = new PointF01[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            points[i] = new PointF01(
                (float)((samples[i].At - samples[0].At).TotalSeconds / span),
                (float)((samples[i].Percent - min) / (max - min)));
        }
        return points;
    }

    private static IEnumerable<(WindowKind Kind, QuotaWindow Window)> Windows(QuotaSnapshot snapshot)
    {
        if (snapshot.Session is not null) yield return (WindowKind.Session, snapshot.Session);
        if (snapshot.Weekly is not null) yield return (WindowKind.Weekly, snapshot.Weekly);
    }

    private static string ResetShort(
        IReadOnlyList<(WindowKind Kind, QuotaWindow Window)> windows, DateTimeOffset now, Strings strings)
    {
        var reset = windows
            .Select(pair => pair.Window.ResetsAt)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .FirstOrDefault();
        if (reset == default) return strings.ResetUnknown;

        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return strings.ResetShort.Replace("{0}", strings.Resetting);
        return string.Format(CultureInfo.InvariantCulture, strings.ResetShort,
            QuotaFormatter.Duration(remaining, strings));
    }
}
