using LimitTray.Core.Presentation;

namespace LimitTray.App;

/// <summary>Core decides colours as Rgb; this is the only place they become UI types.</summary>
public static class Brushes
{
    public static System.Drawing.Color ToDrawing(Rgb c, byte alpha = 255) =>
        System.Drawing.Color.FromArgb(alpha, c.R, c.G, c.B);

    public static System.Windows.Media.Color ToMedia(Rgb c, byte alpha = 255) =>
        System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);

    public static System.Windows.Media.SolidColorBrush Solid(Rgb c, byte alpha = 255)
    {
        var brush = new System.Windows.Media.SolidColorBrush(ToMedia(c, alpha));
        brush.Freeze();
        return brush;
    }
}
