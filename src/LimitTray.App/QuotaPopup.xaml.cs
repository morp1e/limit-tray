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
using LimitTray.Core.Settings;

namespace LimitTray.App;

public partial class QuotaPopup : Window
{
    private const double PageWidth = 360;
    private int _slideGeneration;

    private readonly PanelViewModel _panel;
    private readonly SettingsViewModel _settings;
    private readonly App _app;

    public QuotaPopup(App app, Strings strings, UsageHistory history)
    {
        _app = app;
        _panel = new PanelViewModel(app, history, strings, ShowSettings);
        _settings = new SettingsViewModel(app, strings, ShowPanel);
        InitializeComponent();

        PanelHost.Content = new PanelPage { DataContext = _panel };
        SettingsHost.Content = new SettingsPage { DataContext = _settings };
        SourceInitialized += (_, _) => ApplyBackdrop(IsDarkTheme(_app.Settings.Theme));
        SizeChanged += (_, _) => PositionNearTray();
    }

    /// <summary>
    /// Native rounded corners and no border line, plus the brand glows. The acrylic
    /// backdrop that lived here in the v0.3 development branch was removed on
    /// 2026-09-20: it worked only through the Windows 11 system backdrop, and even there
    /// the difference on screen was not worth a setting.
    /// </summary>
    public void ApplyBackdrop(bool dark)
    {
        WindowBackdrop.ApplyNativeShape(this, dark);
        PaintGlows(dark);
    }

    /// <summary>
    /// The two radial brand glows from the mockup: Claude top-left, Codex bottom-right.
    /// Colours come from Palette so a brand change is one edit in Core.
    /// </summary>
    private void PaintGlows(bool dark)
    {
        var strength = dark ? (byte)0x38 : (byte)0x22;
        GlowTopLeft.Fill = Radial(Palette.ClaudeBrand, strength);
        GlowBottomRight.Fill = Radial(Palette.CodexBrand, strength);
    }

    private static RadialGradientBrush Radial(Rgb colour, byte alpha)
    {
        var brush = new RadialGradientBrush(
            LimitTray.App.Brushes.ToMedia(colour, alpha), LimitTray.App.Brushes.ToMedia(colour, 0))
        {
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.Freeze();
        return brush;
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
        // A completion from an earlier slide must not collapse what a later slide brought
        // in; reversing within 150 ms used to leave the popup blank.
        var generation = ++_slideGeneration;
        outgoingAnimation.Completed += (_, _) =>
        {
            if (generation == _slideGeneration) outgoing.Visibility = Visibility.Collapsed;
        };
        outgoingTransform.BeginAnimation(TranslateTransform.XProperty, outgoingAnimation);
        incomingTransform.BeginAnimation(TranslateTransform.XProperty, incomingAnimation);
    }

    /// <summary>
    /// Bottom-right of the work area of the screen the cursor is on, which is the screen
    /// whose tray was clicked. WorkArea alone would always pick the primary monitor.
    /// </summary>
    private void PositionNearTray()
    {
        var height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var right = work.Right / dpi.DpiScaleX;
        var bottom = work.Bottom / dpi.DpiScaleY;
        Left = right - Width - 12;
        Top = bottom - height - 12;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private static bool IsDarkTheme(LimitTray.Core.Settings.ThemeMode mode) => mode switch
    {
        LimitTray.Core.Settings.ThemeMode.Light => false,
        LimitTray.Core.Settings.ThemeMode.Dark => true,
        _ => !SystemTheme.IsLight(),
    };
}
