using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

public partial class QuotaPopup : Window
{
    public QuotaPopup() => InitializeComponent();

    public void Show(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        ProvidersPanel.Children.Clear();

        foreach (var snapshot in snapshots)
            ProvidersPanel.Children.Add(BuildProviderBlock(snapshot, now));

        FooterText.Text = snapshots.Count == 0
            ? "Henuz veri yok"
            : QuotaFormatter.Age(snapshots.Max(s => s.FetchedAt), now);

        PositionNearTray();
        Show();
        Activate();
    }

    private static UIElement BuildProviderBlock(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        panel.Children.Add(new TextBlock
        {
            Text = TitleFor(snapshot.Provider),
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (snapshot.Session is null && snapshot.Weekly is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = QuotaFormatter.HealthText(snapshot),
                Foreground = new SolidColorBrush(Color.FromRgb(235, 87, 87)),
                FontSize = 12,
            });
            return panel;
        }

        AddWindowRow(panel, "Session", "5 saatlik pencere", snapshot.Session, snapshot, now);
        AddWindowRow(panel, "Weekly", "7 gunluk pencere", snapshot.Weekly, snapshot, now);
        return panel;
    }

    private static void AddWindowRow(
        System.Windows.Controls.Panel parent, string title, string subtitle, QuotaWindow? window,
        QuotaSnapshot snapshot, DateTimeOffset now)
    {
        if (window is null) return;

        var stale = snapshot.Health == HealthState.Stale;
        var color = TrayIconRenderer.ColorFor(QuotaFormatter.SeverityFor(window.Percent));
        var brush = new SolidColorBrush(Color.FromArgb(
            stale ? (byte)120 : (byte)255, color.R, color.G, color.B));

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var percent = new TextBlock
        {
            Text = QuotaFormatter.Percent(window.Percent),
            Foreground = brush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
        DockPanel.SetDock(percent, Dock.Right);
        header.Children.Add(percent);
        header.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = stale ? Brushes.Gray : Brushes.White,
            FontSize = 13,
        });

        parent.Children.Add(header);
        parent.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var track = new Border
        {
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var fill = new Border
        {
            Height = 6,
            Background = brush,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(100, window.Percent)) / 100.0 * 288,
        };
        track.Child = fill;
        parent.Children.Add(track);

        parent.Children.Add(new TextBlock
        {
            Text = QuotaFormatter.ResetsIn(window.ResetsAt, now)
                   + (stale ? " · " + QuotaFormatter.HealthText(snapshot) : ""),
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10),
        });
    }

    private static string TitleFor(string provider) => provider switch
    {
        "claude" => "Claude Usage",
        "codex" => "Codex Usage",
        _ => provider,
    };

    private void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
