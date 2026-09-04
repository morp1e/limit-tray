using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

/// <summary>
/// Draws the highest percentage as a gauge, and a question mark when there is no
/// number to draw. The gauge is open at the bottom and matches the application icon,
/// so the thing in the tray and the thing in the taskbar read as the same object.
/// </summary>
public static class TrayIconRenderer
{
    private const int Size = 32;

    /// <summary>A 270 degree sweep, leaving the gap at the bottom.</summary>
    private const float StartAngle = 135f;
    private const float TotalSweep = 270f;

    public static RenderedIcon Render(double? percent, bool hasUnhealthy = false)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Grayscale antialiasing, not ClearType: subpixel rendering on a
            // transparent bitmap fringes the glyph with colour.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new Rectangle(3, 3, Size - 7, Size - 7);

            using var track = new Pen(
                hasUnhealthy
                    ? Color.FromArgb(150, 235, 87, 87)
                    : Color.FromArgb(70, 255, 255, 255),
                4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawArc(track, rect, StartAngle, TotalSweep);

            if (percent is null)
            {
                using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(230, 200, 200, 200));
                g.DrawString("?", font, brush, new PointF(11f, 7f));
            }
            else
            {
                using var arc = new Pen(ColorFor(QuotaFormatter.SeverityFor(percent.Value)), 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                var sweep = (float)(Math.Clamp(percent.Value, 0, 100) / 100.0 * TotalSweep);
                if (sweep > 0) g.DrawArc(arc, rect, StartAngle, sweep);
            }
        }

        return new RenderedIcon(bitmap.GetHicon());
    }

    public static Color ColorFor(QuotaSeverity severity) => severity switch
    {
        QuotaSeverity.Warning => Color.FromArgb(255, 235, 87, 87),
        QuotaSeverity.Caution => Color.FromArgb(255, 240, 180, 41),
        _ => Color.FromArgb(255, 80, 200, 120),
    };
}
