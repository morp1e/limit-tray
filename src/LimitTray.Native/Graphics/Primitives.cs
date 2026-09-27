namespace LimitTray.Native.Graphics;

/// <summary>Straight (not premultiplied) sRGB colour with alpha.</summary>
internal readonly record struct Colour(byte R, byte G, byte B, byte A = 255)
{
    public Colour WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Multiplies the existing alpha, for opacity layered on a colour that already has one.</summary>
    public Colour Fade(double opacity) => this with { A = (byte)Math.Round(A * Math.Clamp(opacity, 0, 1)) };

    public static Colour FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    public static readonly Colour Transparent = new(0, 0, 0, 0);
    public static readonly Colour White = new(255, 255, 255);
    public static readonly Colour Black = new(0, 0, 0);
}

internal readonly record struct PointF(float X, float Y);

internal readonly record struct SizeF(float W, float H);

internal readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public float CentreX => X + W / 2;
    public float CentreY => Y + H / 2;

    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    public RectF Inflate(float dx, float dy) => new(X - dx, Y - dy, W + 2 * dx, H + 2 * dy);

    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);

    public static readonly RectF Empty = new(0, 0, 0, 0);
}

internal enum TextAlign { Leading, Centre, Trailing }

internal enum FontWeight { Normal = 400, SemiBold = 600, Bold = 700 }

/// <summary>A text format. Records compare by value, so equal styles share one DirectWrite format.</summary>
internal sealed record TextStyle(
    float SizeDip,
    FontWeight Weight = FontWeight.Normal,
    bool Tabular = false,
    bool SmallCaps = false,
    bool Wrap = false,
    string? Family = null);
