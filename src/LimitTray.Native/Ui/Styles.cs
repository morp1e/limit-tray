using LimitTray.Native.Graphics;

namespace LimitTray.Native.Ui;

/// <summary>Text measurement, implemented by <see cref="Canvas"/> and by a fake in tests.</summary>
internal interface ITextMeasure
{
    SizeF Measure(string text, TextStyle style, float maxWidth = float.MaxValue);
    float LineHeight(TextStyle style);
}

/// <summary>The text styles of the v0.3 XAML, by role. WPF's default FontSize is 12.</summary>
internal static class Styles
{
    public static readonly TextStyle Body = new(12);
    public static readonly TextStyle BodySemiTabular = new(12, FontWeight.SemiBold, Tabular: true);
    public static readonly TextStyle RingNumber = new(12, FontWeight.Bold, Tabular: true);
    public static readonly TextStyle CardTitle = new(15, FontWeight.SemiBold);
    public static readonly TextStyle Brand = new(15, FontWeight.Bold, SmallCaps: true);
    public static readonly TextStyle PageTitle = new(18, FontWeight.Bold);
    public static readonly TextStyle Small = new(11);
    public static readonly TextStyle SmallWrap = new(11, Wrap: true);
    public static readonly TextStyle Group = new(10, FontWeight.Bold, SmallCaps: true);
    // The glyphs (↻ ⚙ ‹ ⌁) are not in Segoe UI Variable; DirectWrite's fallback draws them,
    // as WPF's did (forcing Segoe UI Symbol drew a thinner gear). Colour glyphs stay off.
    public static readonly TextStyle HeaderIcon = new(18);
    public static readonly TextStyle TerminalIcon = new(14);

    /// <summary>
    /// v0.3's IconButtonStyle template never bound Padding (its ContentPresenter has no
    /// Margin), so an icon button is its glyph plus the 1 DIP border on each side. Measured
    /// on the v0.3.2 screenshot: header 26 DIPs (24 + 2), gear and refresh centred where
    /// glyph + 2 puts them. An earlier reading that added the padding was 2-5 DIPs off.
    /// </summary>
    public static SizeF IconButton(ITextMeasure m, string glyph, TextStyle style, float scale) => new(
        Snap.ToPixels(m.Measure(glyph, style).W, scale) + 2,
        Snap.ToPixels(m.LineHeight(style), scale) + 2);
}

internal static class Snap
{
    /// <summary>WPF's UseLayoutRounding: sizes land on whole device pixels.</summary>
    public static float ToPixels(float dip, float scale) => MathF.Round(dip * scale) / scale;
}
