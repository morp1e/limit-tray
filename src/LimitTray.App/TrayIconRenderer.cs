using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using LimitTray.Core.Presentation;

namespace LimitTray.App;

/// <summary>En yuksek yuzdeyi halka olarak cizer. Deger yoksa soru isareti cizer.</summary>
public static class TrayIconRenderer
{
    private const int Size = 32;

    public static Icon Render(double? percent, bool hasUnhealthy = false)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new Rectangle(3, 3, Size - 7, Size - 7);

            using var track = new Pen(
                hasUnhealthy
                    ? Color.FromArgb(150, 235, 87, 87)
                    : Color.FromArgb(70, 255, 255, 255),
                4f);
            g.DrawEllipse(track, rect);

            if (percent is null)
            {
                using var font = new Font("Segoe UI", 14f, FontStyle.Bold,
                    GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(230, 200, 200, 200));
                g.DrawString("?", font, brush, new PointF(11f, 8f));
            }
            else
            {
                using var arc = new Pen(ColorFor(QuotaFormatter.SeverityFor(percent.Value)), 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                var sweep = (float)(Math.Clamp(percent.Value, 0, 100) / 100.0 * 360.0);
                if (sweep > 0) g.DrawArc(arc, rect, -90f, sweep);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static Color ColorFor(QuotaSeverity severity) => severity switch
    {
        QuotaSeverity.Warning => Color.FromArgb(255, 235, 87, 87),
        QuotaSeverity.Caution => Color.FromArgb(255, 240, 180, 41),
        _ => Color.FromArgb(255, 80, 200, 120),
    };
}
