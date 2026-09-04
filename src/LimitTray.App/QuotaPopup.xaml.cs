using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

public partial class QuotaPopup : Window
{
    /// <summary>
    /// The real width available inside the window: 320 minus the two border pixels
    /// minus the 16 pixel margin on each side. Pinning content to a wider value than
    /// this pushes the right-aligned percentage against the frame.
    /// </summary>
    private const double ContentWidth = 286;
    private const double SparklineHeight = 22;
    private const int MinimumSparklineSamples = 5;
    private const double MinimumSparklineRange = 1.0;

    private readonly Strings _strings;
    private readonly UsageHistory _history;

    public QuotaPopup(Strings strings, UsageHistory history)
    {
        _strings = strings;
        _history = history;
        InitializeComponent();

        // SizeToContent="Height" means the final height only exists after a layout pass.
        // Repositioning whenever the size changes keeps the panel anchored to the tray
        // however tall the content turns out to be.
        SizeChanged += (_, _) => PositionNearTray();
    }

    public void Show(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        var opening = !IsVisible;

        ProvidersPanel.Children.Clear();

        foreach (var snapshot in snapshots)
            ProvidersPanel.Children.Add(BuildProviderBlock(snapshot, now));

        FooterText.Text = snapshots.Count == 0
            ? _strings.NoData
            : QuotaFormatter.Age(snapshots.Max(s => s.FetchedAt), now, _strings);

        if (opening)
        {
            // The window is realised off-screen first. Positioning before Show() reads
            // ActualHeight as 0, which put the panel at the bottom edge of the work area
            // and left only a sliver visible until a second click -- issue #1.
            Left = -32000;
            Top = -32000;
            Show();
        }

        UpdateLayout();
        PositionNearTray();

        // Only the opening click takes focus. Re-activating on every refresh would pull
        // focus away from whatever the user is typing every two minutes.
        if (opening) Activate();
    }

    private UIElement BuildProviderBlock(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var panel = new StackPanel
        {
            Width = ContentWidth,
            Margin = new Thickness(0, 0, 0, 16),
        };

        panel.Children.Add(Text(
            QuotaFormatter.ProviderTitle(snapshot.Provider, _strings),
            15, Brushes.White, FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));

        if (snapshot.Session is null && snapshot.Weekly is null)
        {
            panel.Children.Add(Text(
                QuotaFormatter.HealthText(snapshot, _strings),
                12, new SolidColorBrush(Color.FromRgb(235, 87, 87)),
                FontWeights.Normal, new Thickness(0)));
            return panel;
        }

        AddWindowRow(panel, WindowKind.Session, snapshot.Session, snapshot, now);
        AddWindowRow(panel, WindowKind.Weekly, snapshot.Weekly, snapshot, now);
        return panel;
    }

    /// <summary>
    /// Every line of text in the panel is built here, with an explicit width and
    /// wrapping turned on.
    ///
    /// This is not decoration. A StackPanel takes its layout width from its widest
    /// child, so one long unwrapped line stretches the whole column past the window and
    /// pushes anything right-aligned clean off the edge. That is exactly how the
    /// percentage silently vanished from the header row.
    /// </summary>
    private static TextBlock Text(
        string text, double size, Brush foreground, FontWeight weight, Thickness margin) =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = foreground,
            FontWeight = weight,
            Margin = margin,
            Width = ContentWidth,
            TextWrapping = TextWrapping.Wrap,
        };

    private void AddWindowRow(
        System.Windows.Controls.Panel parent, WindowKind kind, QuotaWindow? window,
        QuotaSnapshot snapshot, DateTimeOffset now)
    {
        if (window is null) return;

        var stale = snapshot.Health == HealthState.Stale;
        var colour = TrayIconRenderer.ColorFor(QuotaFormatter.SeverityFor(window.Percent));
        var brush = new SolidColorBrush(Color.FromArgb(
            stale ? (byte)120 : (byte)255, colour.R, colour.G, colour.B));

        parent.Children.Add(BuildHeader(kind, window, brush, stale));

        parent.Children.Add(Text(
            QuotaFormatter.WindowSubtitle(kind, _strings),
            11, Brushes.Gray, FontWeights.Normal, new Thickness(0, 0, 0, 4)));

        parent.Children.Add(BuildBar(window, brush));

        var sparkline = BuildSparkline(snapshot.Provider, kind, colour);
        if (sparkline is not null) parent.Children.Add(sparkline);

        parent.Children.Add(Text(
            QuotaFormatter.ResetsIn(window.ResetsAt, now, _strings)
            + (stale ? QuotaFormatter.Separator + QuotaFormatter.HealthText(snapshot, _strings) : ""),
            11, Brushes.Gray, FontWeights.Normal, new Thickness(0, 0, 0, 2)));

        AddBurnRate(parent, snapshot, kind, now);
    }

    /// <summary>
    /// The window name on the left, its percentage hard against the right.
    /// </summary>
    private UIElement BuildHeader(WindowKind kind, QuotaWindow window, Brush brush, bool stale)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };

        var percent = new TextBlock
        {
            Text = QuotaFormatter.Percent(window.Percent, _strings),
            Foreground = brush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
        DockPanel.SetDock(percent, Dock.Right);
        header.Children.Add(percent);

        header.Children.Add(new TextBlock
        {
            Text = QuotaFormatter.WindowTitle(kind, _strings),
            Foreground = stale ? Brushes.Gray : Brushes.White,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return header;
    }

    private static UIElement BuildBar(QuotaWindow window, Brush brush)
    {
        var track = new Border
        {
            Width = ContentWidth,
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 0, 4),
        };

        track.Child = new Border
        {
            Height = 6,
            Background = brush,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(100, window.Percent)) / 100.0 * ContentWidth,
        };

        return track;
    }

    /// <summary>
    /// The measured pace and what it implies. Nothing is drawn when the history cannot
    /// support a projection -- an empty line is honest, an invented number is not.
    /// </summary>
    private void AddBurnRate(
        System.Windows.Controls.Panel parent, QuotaSnapshot snapshot, WindowKind kind,
        DateTimeOffset now)
    {
        if (snapshot.Health != HealthState.Fresh) return;

        var estimate = _history.Estimate(snapshot.Provider, kind, now);
        if (estimate is null) return;

        parent.Children.Add(Text(
            QuotaFormatter.BurnRate(estimate, _strings),
            11, new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            FontWeights.Normal, new Thickness(0, 0, 0, 10)));
    }

    /// <summary>
    /// A trace of the retained observations, plotted against real time so a gap in the
    /// data reads as a gap. Drawn only when there are enough points and the value has
    /// actually moved; a flat line across a panel is decoration, not information.
    /// </summary>
    private UIElement? BuildSparkline(string provider, WindowKind kind, System.Drawing.Color colour)
    {
        var samples = _history.Samples(provider, kind);
        if (samples.Count < MinimumSparklineSamples) return null;

        var min = samples.Min(s => s.Percent);
        var max = samples.Max(s => s.Percent);
        if (max - min < MinimumSparklineRange) return null;

        var span = (samples[^1].At - samples[0].At).TotalSeconds;
        if (span <= 0) return null;

        var points = new PointCollection(samples.Count);
        foreach (var sample in samples)
        {
            var x = (sample.At - samples[0].At).TotalSeconds / span * (ContentWidth - 2) + 1;
            var y = SparklineHeight - 1
                    - (sample.Percent - min) / (max - min) * (SparklineHeight - 2);
            points.Add(new Point(x, y));
        }

        var line = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromArgb(150, colour.R, colour.G, colour.B)),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };

        // The polyline sits in a fixed canvas so its stroke cannot make the element
        // wider than the column it lives in.
        var host = new Canvas
        {
            Width = ContentWidth,
            Height = SparklineHeight,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        host.Children.Add(line);
        return host;
    }

    private void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        var height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;

        Left = area.Right - Width - 12;
        Top = area.Bottom - height - 12;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
