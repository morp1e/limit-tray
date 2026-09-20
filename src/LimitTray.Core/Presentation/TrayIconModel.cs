using LimitTray.Core.Model;
using LimitTray.Core.Settings;

namespace LimitTray.Core.Presentation;

public sealed record TrayBar(double Percent, Rgb Colour, byte Opacity);

/// <summary>
/// What the tray icon draws, decided here so the renderer is only pixels. A null bar
/// means "draw the question mark": the value is unknown, and unknown is never zero.
/// </summary>
public sealed record TrayIconModel(
    TrayIconStyle Style,
    TrayBar? Primary,
    TrayBar? Left,
    TrayBar? Right,
    bool HasUnhealthy);

public static class TrayIconModelBuilder
{
    public static TrayIconModel Build(IReadOnlyList<QuotaSnapshot> snapshots, AppSettings settings)
    {
        var unhealthy = QuotaFormatter.HasUnhealthy(snapshots);

        if (settings.TrayStyle == TrayIconStyle.DualBar)
            return new TrayIconModel(
                TrayIconStyle.DualBar, null,
                Fullest(Find(snapshots, "claude"), settings.Thresholds),
                Fullest(Find(snapshots, "codex"), settings.Thresholds),
                unhealthy);

        var primary = settings.TraySource switch
        {
            TrayIconSource.Highest => Highest(snapshots, settings.Thresholds),
            TrayIconSource.ClaudeSession => Pick(Find(snapshots, "claude"), WindowKind.Session, settings.Thresholds),
            TrayIconSource.ClaudeWeekly => Pick(Find(snapshots, "claude"), WindowKind.Weekly, settings.Thresholds),
            TrayIconSource.CodexSession => Pick(Find(snapshots, "codex"), WindowKind.Session, settings.Thresholds),
            TrayIconSource.CodexWeekly => Pick(Find(snapshots, "codex"), WindowKind.Weekly, settings.Thresholds),
            _ => null,
        };

        // A chosen source that cannot be shown is itself an unhealthy state for the icon,
        // even when the other provider is fine. The icon must not quietly show the other one.
        if (primary is null && settings.TraySource != TrayIconSource.Highest && snapshots.Count > 0)
            unhealthy = true;

        return new TrayIconModel(settings.TrayStyle, primary, null, null, unhealthy);
    }

    private static QuotaSnapshot? Find(IReadOnlyList<QuotaSnapshot> snapshots, string provider) =>
        snapshots.FirstOrDefault(s => s.Provider == provider);

    private static bool Usable(QuotaSnapshot? s) =>
        s is not null && s.Health is HealthState.Fresh or HealthState.Stale;

    private static TrayBar Bar(QuotaSnapshot s, QuotaWindow w, QuotaThresholds t) =>
        new(w.Percent,
            Theme.ColourFor(s.Provider, QuotaFormatter.SeverityFor(w.Percent, t), s.Health),
            Theme.OpacityFor(s.Health));

    private static TrayBar? Pick(QuotaSnapshot? s, WindowKind kind, QuotaThresholds t)
    {
        if (!Usable(s)) return null;
        var w = kind == WindowKind.Session ? s!.Session : s!.Weekly;
        return w is null ? null : Bar(s, w, t);
    }

    private static TrayBar? Fullest(QuotaSnapshot? s, QuotaThresholds t)
    {
        if (!Usable(s)) return null;
        QuotaWindow? best = null;
        foreach (var w in new[] { s!.Session, s.Weekly })
            if (w is not null && (best is null || w.Percent > best.Percent)) best = w;
        return best is null ? null : Bar(s, best, t);
    }

    private static TrayBar? Highest(IReadOnlyList<QuotaSnapshot> snapshots, QuotaThresholds t)
    {
        TrayBar? best = null;
        foreach (var s in snapshots)
        {
            var candidate = Fullest(s, t);
            if (candidate is not null && (best is null || candidate.Percent > best.Percent)) best = candidate;
        }
        return best;
    }
}
