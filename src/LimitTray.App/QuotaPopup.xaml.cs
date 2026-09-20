using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LimitTray.App.ViewModels;
using LimitTray.App.Views;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

public partial class QuotaPopup : Window
{
    private const double PageWidth = 360;

    private readonly PanelViewModel _panel;
    private readonly SettingsViewModel _settings;

    public QuotaPopup(App app, Strings strings, UsageHistory history)
    {
        _panel = new PanelViewModel(app, history, strings, ShowSettings);
        _settings = new SettingsViewModel(app, strings, ShowPanel);
        InitializeComponent();

        PanelHost.Content = new PanelPage { DataContext = _panel };
        SettingsHost.Content = new SettingsPage { DataContext = _settings };
        SizeChanged += (_, _) => PositionNearTray();
    }

    public void Show(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        var opening = !IsVisible;
        Update(snapshots, now);
        ShowPanel();

        if (opening)
        {
            Left = -32000;
            Top = -32000;
            base.Show();
        }

        UpdateLayout();
        PositionNearTray();
        if (opening) Activate();
    }

    public void Update(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now) =>
        _panel.Update(snapshots, now);

    public void UpdateStrings(Strings strings)
    {
        _panel.UpdateStrings(strings);
        _settings.UpdateStrings(strings);
    }

    public void ShowSettings()
    {
        var opening = !IsVisible;
        if (opening)
        {
            Left = -32000;
            Top = -32000;
            base.Show();
        }

        SlideTo(showSettings: true);
        UpdateLayout();
        PositionNearTray();
        if (opening) Activate();
    }

    public void ShowPanel() => SlideTo(showSettings: false);

    private void SlideTo(bool showSettings)
    {
        var duration = SystemParameters.ClientAreaAnimation
            ? new Duration(TimeSpan.FromMilliseconds(150))
            : new Duration(TimeSpan.Zero);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var outgoing = showSettings ? PanelHost : SettingsHost;
        var incoming = showSettings ? SettingsHost : PanelHost;
        var outgoingTransform = new TranslateTransform();
        var incomingTransform = new TranslateTransform(showSettings ? PageWidth : -PageWidth, 0);
        outgoing.RenderTransform = outgoingTransform;
        incoming.RenderTransform = incomingTransform;
        incoming.Visibility = Visibility.Visible;

        var outgoingAnimation = new DoubleAnimation(0, showSettings ? -PageWidth : PageWidth, duration)
        {
            EasingFunction = ease,
        };
        var incomingAnimation = new DoubleAnimation(showSettings ? PageWidth : -PageWidth, 0, duration)
        {
            EasingFunction = ease,
        };
        outgoingAnimation.Completed += (_, _) => outgoing.Visibility = Visibility.Collapsed;
        outgoingTransform.BeginAnimation(TranslateTransform.XProperty, outgoingAnimation);
        incomingTransform.BeginAnimation(TranslateTransform.XProperty, incomingAnimation);
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
