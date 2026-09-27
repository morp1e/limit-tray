using LimitTray.Core.Presentation;
using LimitTray.Native.Graphics;

namespace LimitTray.Native.Ui;

/// <summary>
/// The v0.3 theme dictionaries (Themes/Dark.xaml, Themes/Light.xaml) and Brushes.cs.
/// Quota colours are never decided here: they come from Theme.ColourFor and are only
/// tinted (Lighter) or given an alpha, exactly as Brushes.cs did.
/// </summary>
internal sealed record Palette(
    bool Dark,
    Colour Surface,
    Colour Card,
    Colour CardBorder,
    Colour CardHoverBorder,
    Colour Text,
    Colour MutedText,
    Colour Track,
    Colour Separator,
    Colour Accent,
    Colour Popup,
    byte GlowStrength)
{
    public static readonly Palette DarkTheme = new(
        Dark: true,
        Surface: Colour.FromArgb(0xFF0D0F14),
        Card: Colour.FromArgb(0x0DFFFFFF),
        CardBorder: Colour.FromArgb(0x14FFFFFF),
        CardHoverBorder: Colour.FromArgb(0x33FFFFFF),
        Text: Colour.FromArgb(0xFFEEF1F6),
        MutedText: Colour.FromArgb(0xFF8A93A3),
        Track: Colour.FromArgb(0x1AFFFFFF),
        Separator: Colour.FromArgb(0x0FFFFFFF),
        Accent: Colour.FromArgb(0xFF10A37F),
        Popup: Colour.FromArgb(0xFF161920),
        GlowStrength: 0x38);

    public static readonly Palette LightTheme = new(
        Dark: false,
        Surface: Colour.FromArgb(0xFFF4F5F8),
        Card: Colour.FromArgb(0xFFFFFFFF),
        CardBorder: Colour.FromArgb(0x14000000),
        CardHoverBorder: Colour.FromArgb(0x33000000),
        Text: Colour.FromArgb(0xFF14161B),
        MutedText: Colour.FromArgb(0xFF5B6270),
        Track: Colour.FromArgb(0x14000000),
        Separator: Colour.FromArgb(0x0F000000),
        Accent: Colour.FromArgb(0xFF10A37F),
        Popup: Colour.FromArgb(0xFFFFFFFF),
        GlowStrength: 0x22);

    public static Colour From(Rgb c, byte alpha = 255) => new(c.R, c.G, c.B, alpha);

    /// <summary>The same hue pushed a step towards white, used for the bar tip and the number.</summary>
    public static Rgb Lighter(Rgb c, double amount = 0.28) => new(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));
}
