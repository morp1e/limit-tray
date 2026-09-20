using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LimitTray.App.Views;

public sealed class PercentToArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double raw || double.IsNaN(raw)) return Geometry.Empty;
        var percent = Math.Clamp(raw, 0, 100);
        if (percent <= 0) return Geometry.Empty;

        const double center = 26;
        const double radius = 21;
        var start = PointOnCircle(center, radius, -90);
        var endAngle = -90 + (percent / 100 * 360);
        var end = PointOnCircle(center, radius, endAngle);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, percent > 50,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Point PointOnCircle(double center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }
}

public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Either value is DependencyProperty.UnsetValue while a template is being torn
        // down or rebound; converting that blindly threw on the UI thread.
        if (values.Length < 2 || values[0] is not double width || values[1] is not double percent
            || double.IsNaN(width) || double.IsNaN(percent))
            return 0.0;
        return Math.Clamp(percent, 0, 100) / 100.0 * Math.Max(0, width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullableToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Visible only when the card is expanded and the value exists.</summary>
public sealed class ExpandedAndPresentConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is true && values[1] is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BooleanToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public partial class PanelPage : UserControl
{
    private static readonly DependencyProperty LastWidthProperty =
        DependencyProperty.RegisterAttached("LastWidth", typeof(double), typeof(PanelPage),
            new FrameworkPropertyMetadata(double.NaN));
    private static readonly DependencyProperty LastRingPercentProperty =
        DependencyProperty.RegisterAttached("LastRingPercent", typeof(double), typeof(PanelPage),
            new FrameworkPropertyMetadata(double.NaN));
    private static readonly DependencyProperty RingTimerProperty =
        DependencyProperty.RegisterAttached("RingTimer", typeof(DispatcherTimer), typeof(PanelPage));

    public PanelPage() => InitializeComponent();

    private void OnCardMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindParent<Button>(source) is not null) return;

        if (sender is Border { DataContext: ViewModels.ProviderCardViewModel card })
            card.IsExpanded = !card.IsExpanded;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void OnBarTargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is not Border bar) return;

        var target = bar.Width;
        if (double.IsNaN(target) || double.IsInfinity(target)) return;
        var previous = (double)bar.GetValue(LastWidthProperty);
        bar.SetValue(LastWidthProperty, target);
        if (double.IsNaN(previous) || !SystemParameters.ClientAreaAnimation)
        {
            bar.BeginAnimation(WidthProperty, null);
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            previous, target, AnimationDuration())
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        bar.BeginAnimation(WidthProperty, animation);
    }

    private void OnRingTargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is not Path path || path.DataContext is not ViewModels.ProviderCardViewModel card)
            return;

        var target = card.RingPercent;
        var previous = (double)path.GetValue(LastRingPercentProperty);
        path.SetValue(LastRingPercentProperty, target);
        (path.GetValue(RingTimerProperty) as DispatcherTimer)?.Stop();

        if (double.IsNaN(previous) || !SystemParameters.ClientAreaAnimation ||
            Math.Abs(target - previous) < double.Epsilon)
            return;

        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 300, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var current = previous + (target - previous) * eased;
            path.SetCurrentValue(Path.DataProperty,
                new PercentToArcConverter().Convert(current, typeof(Geometry), string.Empty, CultureInfo.InvariantCulture));
            if (progress >= 1) timer.Stop();
        };
        path.SetValue(RingTimerProperty, timer);
        timer.Start();
    }

    private static Duration AnimationDuration() =>
        SystemParameters.ClientAreaAnimation
            ? new Duration(TimeSpan.FromMilliseconds(300))
            : new Duration(TimeSpan.Zero);
}
