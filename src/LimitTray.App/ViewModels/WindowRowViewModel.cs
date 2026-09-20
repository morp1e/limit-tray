using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App.ViewModels;

public sealed class WindowRowViewModel : ObservableObject
{
    private const int MinimumSparklineSamples = 5;
    private const double MinimumSparklineRange = 1.0;
    private const double SparklineWidth = 252;
    private const double SparklineHeight = 24;

    public WindowRowViewModel(
        WindowKind kind,
        QuotaWindow window,
        QuotaSnapshot snapshot,
        UsageHistory history,
        AppSettings settings,
        Strings strings,
        DateTimeOffset now)
    {
        Kind = kind;
        Label = QuotaFormatter.WindowTitle(kind, strings);
        Percent = Math.Clamp(window.Percent, 0, 100);
        PercentText = QuotaFormatter.Percent(window.Percent, strings);
        var rgb = Theme.ColourFor(
            snapshot.Provider,
            QuotaFormatter.SeverityFor(window.Percent, settings.Thresholds),
            snapshot.Health);
        var alpha = Theme.OpacityFor(snapshot.Health);
        Colour = Brushes.Solid(rgb, alpha);
        BarBrush = Brushes.Gradient(rgb, alpha);
        PercentBrush = Brushes.Solid(Brushes.Lighter(rgb), alpha);
        // Stale and error rows are muted; a glow under grey would only be noise.
        Glow = snapshot.Health == HealthState.Fresh ? Brushes.Glow(rgb) : null;
        ResetText = QuotaFormatter.ResetsIn(window.ResetsAt, now, strings);

        var isExpanded = settings.ExpandedProviders.Contains(snapshot.Provider);
        var estimate = snapshot.Health == HealthState.Fresh
            ? history.Estimate(snapshot.Provider, kind, now)
            : null;
        BurnRateText = !isExpanded || estimate is null ? null : QuotaFormatter.BurnRate(estimate, strings);
        SparklinePoints = isExpanded ? BuildSparkline(history.Samples(snapshot.Provider, kind)) : null;
    }

    public WindowKind Kind { get; }
    public string Label { get; }
    public double Percent { get; }
    public string PercentText { get; }
    public System.Windows.Media.Brush Colour { get; }
    public System.Windows.Media.Brush BarBrush { get; }
    public System.Windows.Media.Brush PercentBrush { get; }
    public System.Windows.Media.Effects.Effect? Glow { get; }
    public string ResetText { get; }
    public string? BurnRateText { get; }
    public PointCollection? SparklinePoints { get; }

    private static PointCollection? BuildSparkline(IReadOnlyList<UsageSample> samples)
    {
        if (samples.Count < MinimumSparklineSamples) return null;

        var min = samples.Min(sample => sample.Percent);
        var max = samples.Max(sample => sample.Percent);
        if (max - min < MinimumSparklineRange) return null;

        var span = (samples[^1].At - samples[0].At).TotalSeconds;
        if (span <= 0) return null;

        var points = new PointCollection(samples.Count);
        foreach (var sample in samples)
        {
            var x = (sample.At - samples[0].At).TotalSeconds / span * (SparklineWidth - 2) + 1;
            var y = SparklineHeight - 1
                    - (sample.Percent - min) / (max - min) * (SparklineHeight - 2);
            points.Add(new Point(x, y));
        }

        return points;
    }
}
