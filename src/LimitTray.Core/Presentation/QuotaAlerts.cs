using LimitTray.Core.Model;

namespace LimitTray.Core.Presentation;

/// <summary>One window that has just crossed into the warning band.</summary>
public sealed record QuotaAlert(string Provider, WindowKind Kind, double Percent);

/// <summary>
/// Decides when a window is worth interrupting the user for.
///
/// The rule is deliberately narrow: one notification per window per fill. Crossing the
/// warning threshold fires once; staying above it is silent; falling back below it arms
/// the next crossing. A quota tool that pops a balloon every two minutes gets muted, and
/// a muted warning is worth nothing.
/// </summary>
public sealed class QuotaAlerts
{
    private readonly Dictionary<(string Provider, WindowKind Kind), bool> _warned = new();
    private readonly object _gate = new();

    /// <summary>
    /// Returns the alerts a snapshot newly justifies. Only Fresh snapshots can raise one:
    /// an error state carries no measurement, and warning on stale data would repeat a
    /// warning the user has already seen.
    /// </summary>
    public IReadOnlyList<QuotaAlert> Inspect(QuotaSnapshot snapshot)
    {
        if (snapshot.Health != HealthState.Fresh) return Array.Empty<QuotaAlert>();

        var alerts = new List<QuotaAlert>(2);

        lock (_gate)
        {
            Consider(snapshot.Provider, WindowKind.Session, snapshot.Session, alerts);
            Consider(snapshot.Provider, WindowKind.Weekly, snapshot.Weekly, alerts);
        }

        return alerts;
    }

    private void Consider(
        string provider, WindowKind kind, QuotaWindow? window, List<QuotaAlert> alerts)
    {
        if (window is null) return;

        var key = (provider, kind);
        var above = QuotaFormatter.SeverityFor(window.Percent) == QuotaSeverity.Warning;

        if (!above)
        {
            _warned[key] = false;
            return;
        }

        if (_warned.TryGetValue(key, out var already) && already) return;

        _warned[key] = true;
        alerts.Add(new QuotaAlert(provider, kind, window.Percent));
    }

    /// <summary>The text of a notification for one alert.</summary>
    public static string Body(QuotaAlert alert, Strings strings) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            strings.WarningNotificationBody,
            QuotaFormatter.ProviderTitle(alert.Provider, strings),
            QuotaFormatter.WindowSubtitle(alert.Kind, strings),
            QuotaFormatter.Percent(alert.Percent, strings));
}
