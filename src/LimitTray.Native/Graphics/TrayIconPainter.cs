using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Graphics;

/// <summary>
/// v0.3's TrayIconRenderer, drawn with the same 32-unit geometry but straight at the
/// tray's pixel size instead of letting the shell shrink a 32 px bitmap. The model
/// decides everything; a null bar is the question mark, never an empty gauge, because
/// empty reads as zero. Direct2D is created for one paint and released straight after:
/// the icon only changes when the model does.
/// </summary>
internal sealed unsafe class TrayIconPainter : ITrayIconPainter
{
    private const float Size = 32;
    private const float StartAngle = 135f;
    private const float TotalSweep = 270f;

    private static readonly TextStyle Digits = new(17, FontWeight.Bold, Family: "Segoe UI");
    private static readonly TextStyle Mark = new(16, FontWeight.Bold, Family: "Segoe UI");
    private static readonly TextStyle HalfMark = new(13, FontWeight.Bold, Family: "Segoe UI");
    private static readonly Colour Track = new(255, 255, 255, 70);
    private static readonly Colour MarkColour = new(200, 200, 200, 230);

    public HICON Paint(TrayIconModel model, int sizePx)
    {
        using var surface = Draw(model, sizePx);
        return ToIcon(surface);
    }

    /// <summary>The icon's premultiplied pixels; the render command writes these to a PNG.</summary>
    public static Surface Draw(TrayIconModel model, int sizePx)
    {
        using var factory = GraphicsFactory.Create();
        using var text = new TextEngine(factory);
        var surface = new Surface(sizePx, sizePx);
        using (var canvas = new Canvas(factory, text, surface, sizePx / Size))
        {
            canvas.Begin();
            canvas.Clear();
            switch (model.Style)
            {
                case TrayIconStyle.Number: DrawNumber(canvas, model); break;
                case TrayIconStyle.DualBar: DrawDualBar(canvas, model); break;
                default: DrawRing(canvas, model); break;
            }
            canvas.End();
        }

        return surface;
    }

    private static void DrawRing(Canvas c, TrayIconModel m)
    {
        // GDI+ DrawArc on Rectangle(3, 3, 25, 25): centre 15.5, radius 12.5, pen 4, round caps.
        var centre = new PointF(15.5f, 15.5f);
        var track = m.HasUnhealthy ? Ui.Palette.From(Palette.Warning, 150) : Track;
        c.Arc(centre, 12.5f, StartAngle, TotalSweep, track, 4);

        if (m.Primary is null)
        {
            QuestionMark(c, new RectF(0, 0, Size, Size), Mark);
            return;
        }

        var sweep = (float)(Math.Clamp(m.Primary.Percent, 0, 100) / 100.0 * TotalSweep);
        if (sweep > 0) c.Arc(centre, 12.5f, StartAngle, sweep, Ui.Palette.From(m.Primary.Colour, m.Primary.Opacity), 4);
    }

    private static void DrawNumber(Canvas c, TrayIconModel m)
    {
        var box = new RectF(1, 1, Size - 2, Size - 2);
        if (m.Primary is null)
        {
            c.FillRoundedRect(box, 7, new Colour(60, 64, 72, 200));
            QuestionMark(c, new RectF(0, 0, Size, Size), Mark);
            return;
        }

        c.FillRoundedRect(box, 7, Ui.Palette.From(m.Primary.Colour, m.Primary.Opacity));
        var percent = (int)Math.Round(Math.Clamp(m.Primary.Percent, 0, 100), MidpointRounding.AwayFromZero);
        if (percent >= 100)
        {
            // A filled block, not "100": three digits do not fit at 16 px and a
            // truncated "10" would be a lie.
            c.FillRect(new RectF(9, 9, Size - 18, Size - 18), Contrast(m.Primary.Colour));
            return;
        }

        c.Text(percent.ToString(System.Globalization.CultureInfo.InvariantCulture), Digits,
            new RectF(0, 1, Size, Size), Contrast(m.Primary.Colour), TextAlign.Centre, centreVertically: true);
    }

    private static void DrawDualBar(Canvas c, TrayIconModel m)
    {
        var left = new RectF(4, 3, 10, Size - 6);
        var right = new RectF(Size - 14, 3, 10, Size - 6);
        VerticalBar(c, m.Left, left);
        VerticalBar(c, m.Right, right);
        // A side with no value is a question mark in that half, never an empty track.
        if (m.Left is null) QuestionMark(c, left, HalfMark);
        if (m.Right is null) QuestionMark(c, right, HalfMark);
    }

    private static void VerticalBar(Canvas c, TrayBar? bar, RectF track)
    {
        c.FillRoundedRect(track, 3, Track);
        if (bar is null) return;

        var height = MathF.Round((float)(Math.Clamp(bar.Percent, 0, 100) / 100.0 * track.H));
        if (height <= 0) return;
        c.FillRoundedRect(new RectF(track.X, track.Bottom - height, track.W, height),
            MathF.Min(3, height / 2), Ui.Palette.From(bar.Colour, bar.Opacity));
    }

    private static void QuestionMark(Canvas c, RectF area, TextStyle style) =>
        c.Text(Glyphs.Unknown, style, area, MarkColour, TextAlign.Centre, centreVertically: true);

    private static Colour Contrast(Rgb c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? new Colour(20, 18, 10) : Colour.White;

    /// <summary>
    /// Icons take straight alpha, the surface holds premultiplied pixels; convert, then
    /// CreateIconIndirect copies both bitmaps, so the surface can go right after.
    /// </summary>
    private static HICON ToIcon(Surface surface)
    {
        foreach (ref var p in surface.Pixels)
        {
            var a = p >> 24;
            if (a is 0 or 255) continue;
            var r = Math.Min(255u, (((p >> 16) & 0xFF) * 255 + a / 2) / a);
            var g = Math.Min(255u, (((p >> 8) & 0xFF) * 255 + a / 2) / a);
            var b = Math.Min(255u, ((p & 0xFF) * 255 + a / 2) / a);
            p = a << 24 | r << 16 | g << 8 | b;
        }

        var mask = PInvoke.CreateBitmap(surface.Width, surface.Height, 1, 1, null);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = surface.Bitmap };
            return PInvoke.CreateIconIndirect(&info);
        }
        finally
        {
            PInvoke.DeleteObject((HGDIOBJ)(nint)mask.Value);
        }
    }
}
