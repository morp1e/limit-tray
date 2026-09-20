using System.Windows.Media;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

/// <summary>
/// Core decides colours as Rgb; this is the only place they become UI types. The
/// lighter tint and the glow are derived here from the colour Core chose, so the
/// decision of which colour still lives in one tested place.
/// </summary>
public static class Brushes
{
    public static System.Drawing.Color ToDrawing(Rgb c, byte alpha = 255) =>
        System.Drawing.Color.FromArgb(alpha, c.R, c.G, c.B);

    public static Color ToMedia(Rgb c, byte alpha = 255) =>
        Color.FromArgb(alpha, c.R, c.G, c.B);

    public static SolidColorBrush Solid(Rgb c, byte alpha = 255)
    {
        var brush = new SolidColorBrush(ToMedia(c, alpha));
        brush.Freeze();
        return brush;
    }

    /// <summary>The same hue pushed a step towards white, used for the bar tip and the number.</summary>
    public static Rgb Lighter(Rgb c, double amount = 0.28) => new(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    /// <summary>Left to right, base colour to its lighter tint.</summary>
    public static LinearGradientBrush Gradient(Rgb c, byte alpha = 255)
    {
        var brush = new LinearGradientBrush(
            ToMedia(c, alpha), ToMedia(Lighter(c), alpha),
            new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    /// <summary>A soft coloured shadow under a bar or ring.</summary>
    public static System.Windows.Media.Effects.DropShadowEffect Glow(Rgb c, double opacity = 0.55)
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = ToMedia(c),
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = opacity,
        };
        effect.Freeze();
        return effect;
    }
}
