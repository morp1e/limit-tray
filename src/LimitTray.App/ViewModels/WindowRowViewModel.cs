using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App.ViewModels;

/// <summary>
/// One quota window inside a provider card. Like the card, the instance is kept and
/// refreshed in place: replacing the row list on every reading regenerated the item
/// containers, and while a new container was still unattached its expanded-content
/// visibility resolved to collapsed for one layout pass, so the whole popup shrank and
/// grew with every Codex notification.
/// </summary>
public sealed class WindowRowViewModel : ObservableObject
{
    private const int MinimumSparklineSamples = 5;
    private const double MinimumSparklineRange = 1.0;
    private const double SparklineWidth = 252;
    private const double SparklineHeight = 24;

    private string _label = string.Empty;
    private double _percent;
    private string _percentText = string.Empty;
    private Brush _colour = System.Windows.Media.Brushes.Transparent;
    private Brush _barBrush = System.Windows.Media.Brushes.Transparent;
    private Brush _percentBrush = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Effects.Effect? _glow;
    private string _resetText = string.Empty;
    private string? _burnRateText;
    private PointCollection? _sparklinePoints;
    private bool _isExpanded;

    public WindowRowViewModel(
        WindowKind kind,
        QuotaWindow window,
        QuotaSnapshot snapshot,
        UsageHistory history,
        AppSettings settings,
        Strings strings,
        DateTimeOffset now,
        bool isExpanded)
    {
        Kind = kind;
        _isExpanded = isExpanded;
        Refresh(window, snapshot, history, settings, strings, now);
    }

    public WindowKind Kind { get; }
    public string Label { get => _label; private set => Set(ref _label, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public Brush Colour { get => _colour; private set => Set(ref _colour, value); }
    public Brush BarBrush { get => _barBrush; private set => Set(ref _barBrush, value); }
    public Brush PercentBrush { get => _percentBrush; private set => Set(ref _percentBrush, value); }
    public System.Windows.Media.Effects.Effect? Glow { get => _glow; private set => Set(ref _glow, value); }
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }
    public string? BurnRateText { get => _burnRateText; private set => Set(ref _burnRateText, value); }
    public PointCollection? SparklinePoints { get => _sparklinePoints; private set => Set(ref _sparklinePoints, value); }

    /// <summary>Mirrors the owning card; the view binds here rather than walking up the tree.</summary>
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    public void Refresh(
        QuotaWindow window,
        QuotaSnapshot snapshot,
        UsageHistory history,
        AppSettings settings,
        Strings strings,
        DateTimeOffset now)
    {
        Label = QuotaFormatter.WindowTitle(Kind, strings);
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

        var estimate = snapshot.Health == HealthState.Fresh
            ? history.Estimate(snapshot.Provider, Kind, now)
            : null;
        BurnRateText = estimate is null ? null : QuotaFormatter.BurnRate(estimate, strings);
        SparklinePoints = BuildSparkline(history.Samples(snapshot.Provider, Kind));
    }

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

        points.Freeze();
        return points;
    }
}
