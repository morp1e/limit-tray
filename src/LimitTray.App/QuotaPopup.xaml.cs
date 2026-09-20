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
        _app.SettingsChanged += OnSettingsChanged;
        SizeChanged += (_, e) =>
        {
            RootClip.Rect = new Rect(0, 0, e.NewSize.Width - 2, e.NewSize.Height - 2);
            PositionNearTray();
        };
    }

    public void ApplyBackdrop(bool dark)
    {
        WindowBackdrop.ApplyNativeShape(this, dark);
        PaintGlows(dark);

        if (FindResource("SurfaceBrush") is not SolidColorBrush surface) return;

        var applied = _app.Settings.GlassEffect
            && WindowBackdrop.TryApplyAcrylic(this, surface.Color);
        if (!applied)
        {
            WindowBackdrop.Clear(this);
            RootBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            return;
        }

        // The blur shows through whatever alpha the surface leaves; 0.78 keeps text
        // readable over a busy desktop while still reading as glass.
        var glass = new SolidColorBrush(surface.Color) { Opacity = 0.78 };
        glass.Freeze();
        RootBorder.Background = glass;
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

    private void OnSettingsChanged(AppSettings settings) => ApplyBackdrop(IsDarkTheme(settings.Theme));

    private static bool IsDarkTheme(LimitTray.Core.Settings.ThemeMode mode) => mode switch
    {
        LimitTray.Core.Settings.ThemeMode.Light => false,
        LimitTray.Core.Settings.ThemeMode.Dark => true,
        _ => !SystemTheme.IsLight(),
    };
}
