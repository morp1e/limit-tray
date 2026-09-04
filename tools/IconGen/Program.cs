using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LimitTray.IconGen;

/// <summary>
/// Draws the application icon and packs it into a multi-resolution .ico file.
///
/// The mark is the same thing the tray icon draws at runtime: a gauge, open at the
/// bottom, with the apostrophe from the name sitting inside it. It is drawn from
/// scratch at every size rather than scaled from one bitmap, because a ring this thin
/// turns to mush when the shell downsamples it. The apostrophe is dropped below 24
/// pixels, where it would be two grey pixels and would only muddy the ring.
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static readonly Color Background = Color.FromArgb(255, 22, 24, 28);
    private static readonly Color Track = Color.FromArgb(255, 52, 56, 63);
    private static readonly Color Arc = Color.FromArgb(255, 79, 201, 127);
    private static readonly Color Mark = Color.FromArgb(255, 232, 234, 237);

    /// <summary>Gauge geometry: a 270 degree sweep leaving the gap at the bottom.</summary>
    private const float StartAngle = 135f;
    private const float TotalSweep = 270f;
    private const float FilledFraction = 0.72f;

    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "assets", "limit.ico");

        output = Path.GetFullPath(output);
        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var frames = Sizes.ToDictionary(size => size, RenderPng);
        WriteIco(output, frames);

        Console.WriteLine($"wrote {output} ({new FileInfo(output).Length} bytes, " +
                          $"{frames.Count} sizes: {string.Join(", ", Sizes)})");
        return 0;
    }

    private static byte[] RenderPng(int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            DrawBackground(g, size);
            DrawGauge(g, size);
            if (size >= 24) DrawApostrophe(g, size);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void DrawBackground(Graphics g, int size)
    {
        var radius = size * 0.22f;
        using var path = RoundedRectangle(new RectangleF(0, 0, size, size), radius);
        using var brush = new SolidBrush(Background);
        g.FillPath(brush, path);
    }

    private static void DrawGauge(Graphics g, int size)
    {
        var stroke = Math.Max(2f, size * 0.115f);
        var inset = size * 0.235f;
        var rect = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);

        using var trackPen = new Pen(Track, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(trackPen, rect, StartAngle, TotalSweep);

        using var arcPen = new Pen(Arc, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(arcPen, rect, StartAngle, TotalSweep * FilledFraction);
    }

    /// <summary>
    /// The apostrophe from "Lim'it": a slanted capsule in the middle of the gauge.
    /// </summary>
    private static void DrawApostrophe(Graphics g, int size)
    {
        var width = size * 0.11f;
        var height = size * 0.26f;
        var centre = size / 2f;

        var body = new RectangleF(-width / 2f, -height / 2f, width, height);
        using var path = RoundedRectangle(body, width / 2f);

        var state = g.Save();
        g.TranslateTransform(centre, centre - size * 0.015f);
        g.RotateTransform(13f);
        using (var brush = new SolidBrush(Mark)) g.FillPath(brush, path);
        g.Restore(state);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Packs PNG frames into an .ico. Every entry carries a PNG payload, which Windows
    /// has accepted at any size since Vista; there is no BMP/DIB path here on purpose.
    /// </summary>
    private static void WriteIco(string path, IReadOnlyDictionary<int, byte[]> frames)
    {
        var ordered = frames.OrderBy(pair => pair.Key).ToList();

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)ordered.Count);

        var offset = 6 + 16 * ordered.Count;
        foreach (var (size, png) in ordered)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // palette entries
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // colour planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in ordered) writer.Write(png);
    }
}
