using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App;

/// <summary>
/// Turns a <see cref="TrayIconModel"/> into pixels. No decision about what to show is
/// made here; that is the model's job and it is tested. A null bar is drawn as the
/// question mark, never as an empty gauge, because empty reads as zero.
/// </summary>
public static class TrayIconRenderer
{
    private const int Size = 32;
    private const float StartAngle = 135f;
    private const float TotalSweep = 270f;

    public static RenderedIcon Render(TrayIconModel model)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            switch (model.Style)
            {
                case TrayIconStyle.Number: DrawNumber(g, model); break;
                case TrayIconStyle.DualBar: DrawDualBar(g, model); break;
                default: DrawRing(g, model); break;
            }
        }
        return new RenderedIcon(bitmap.GetHicon());
    }

    private static void DrawRing(Graphics g, TrayIconModel m)
    {
        var rect = new Rectangle(3, 3, Size - 7, Size - 7);
        using var track = new Pen(TrackColour(m.HasUnhealthy), 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(track, rect, StartAngle, TotalSweep);

        if (m.Primary is null) { DrawQuestionMark(g); return; }

        using var arc = new Pen(Brushes.ToDrawing(m.Primary.Colour, m.Primary.Opacity), 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        var sweep = (float)(Math.Clamp(m.Primary.Percent, 0, 100) / 100.0 * TotalSweep);
        if (sweep > 0) g.DrawArc(arc, rect, StartAngle, sweep);
    }

    private static void DrawNumber(Graphics g, TrayIconModel m)
    {
        var rect = new Rectangle(1, 1, Size - 2, Size - 2);
        using var path = RoundedRect(rect, 7);

        if (m.Primary is null)
        {
            using var dark = new SolidBrush(Color.FromArgb(200, 60, 64, 72));
            g.FillPath(dark, path);
            DrawQuestionMark(g);
            return;
        }

        using var fill = new SolidBrush(Brushes.ToDrawing(m.Primary.Colour, m.Primary.Opacity));
        g.FillPath(fill, path);

        var percent = (int)Math.Round(Math.Clamp(m.Primary.Percent, 0, 100), MidpointRounding.AwayFromZero);
        if (percent >= 100)
        {
            // A filled block, not "100": three digits do not fit at 16 px and a
            // truncated "10" would be a lie.
            using var block = new SolidBrush(Contrast(m.Primary.Colour));
            g.FillRectangle(block, new Rectangle(9, 9, Size - 18, Size - 18));
            return;
        }

        using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Contrast(m.Primary.Colour));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(percent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            font, text, new RectangleF(0, 1, Size, Size), format);
    }

    private static void DrawDualBar(Graphics g, TrayIconModel m)
    {
        DrawVerticalBar(g, m.Left, new Rectangle(4, 3, 10, Size - 6));
        DrawVerticalBar(g, m.Right, new Rectangle(Size - 14, 3, 10, Size - 6));
        if (m.Left is null && m.Right is null) DrawQuestionMark(g);
    }

    private static void DrawVerticalBar(Graphics g, TrayBar? bar, Rectangle track)
    {
        using var trackPath = RoundedRect(track, 3);
        using var trackBrush = new SolidBrush(
            bar is null ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(70, 255, 255, 255));
        g.FillPath(trackBrush, trackPath);

        if (bar is null) return;

        var height = (int)Math.Round(Math.Clamp(bar.Percent, 0, 100) / 100.0 * track.Height);
        if (height <= 0) return;
        var fill = new Rectangle(track.X, track.Bottom - height, track.Width, height);
        using var fillPath = RoundedRect(fill, 3);
        using var fillBrush = new SolidBrush(Brushes.ToDrawing(bar.Colour, bar.Opacity));
        g.FillPath(fillBrush, fillPath);
    }

    private static void DrawQuestionMark(Graphics g)
    {
        using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(230, 200, 200, 200));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString("?", font, brush, new RectangleF(0, 0, Size, Size), format);
    }

    private static Color TrackColour(bool hasUnhealthy) =>
        hasUnhealthy ? Color.FromArgb(150, 229, 72, 77) : Color.FromArgb(70, 255, 255, 255);

    private static Color Contrast(Rgb c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150
            ? Color.FromArgb(255, 20, 18, 10)
            : Color.White;

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
