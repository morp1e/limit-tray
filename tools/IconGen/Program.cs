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
        // icon <path>   -> the multi-resolution .ico
        // banner <path> -> the 1280x640 repository image
        // mark <path> <size> -> a single square PNG of the mark alone
        var command = args.Length > 0 ? args[0] : "icon";
        var output = Path.GetFullPath(args.Length > 1 ? args[1] : DefaultPath(command));

        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        switch (command)
        {
            case "icon":
                WriteIco(output, Sizes.ToDictionary(size => size, RenderPng));
                break;

            case "banner":
                File.WriteAllBytes(output, RenderBanner());
                break;

            case "mark":
                var size = args.Length > 2 ? int.Parse(args[2]) : 256;
                File.WriteAllBytes(output, RenderPng(size));
                break;

            default:
                Console.Error.WriteLine($"unknown command '{command}'; expected icon, banner or mark");
                return 1;
        }

        Console.WriteLine($"wrote {output} ({new FileInfo(output).Length} bytes)");
        return 0;
    }

    private static string DefaultPath(string command) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets",
            command switch
            {
                "banner" => "banner.png",
                "mark" => "mark.png",
                _ => "limit.ico",
            });

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

    /// <summary>
    /// The repository image, at GitHub's social-preview size. The mark sits left of the
    /// wordmark rather than above it, because the preview is cropped vertically in some
    /// surfaces and a stacked layout loses its top half.
    /// </summary>
    private static byte[] RenderBanner()
    {
        const int width = 1280;
        const int height = 640;
        const int mark = 224;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var background = new SolidBrush(Background)) g.FillRectangle(background, 0, 0, width, height);

            using var wordmarkFont = new Font("Segoe UI", 84f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var taglineFont = new Font("Segoe UI", 30f, FontStyle.Regular, GraphicsUnit.Pixel);

            const string wordmark = "Lim'it";
            const string line1 = "Claude Code and Codex CLI usage limits";
            const string line2 = "in your Windows tray";

            // Typographic formatting for both measuring and drawing. The default
            // StringFormat pads each string by a font-dependent amount, so a bold
            // wordmark and a regular tagline end up with visibly different left edges.
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

            // The block is measured and then centred. Hard-coded offsets look centred
            // only for the one string they were tuned against.
            var wordmarkSize = g.MeasureString(wordmark, wordmarkFont, PointF.Empty, format);
            var line1Size = g.MeasureString(line1, taglineFont, PointF.Empty, format);
            var line2Size = g.MeasureString(line2, taglineFont, PointF.Empty, format);
            var textWidth = Math.Max(wordmarkSize.Width, Math.Max(line1Size.Width, line2Size.Width));

            const float gap = 56f;
            var groupWidth = mark + gap + textWidth;
            var left = (width - groupWidth) / 2f;
            var centre = height / 2f;

            // The mark comes from the same renderer as the icon, so the two cannot drift.
            using (var markImage = Image.FromStream(new MemoryStream(RenderPng(mark))))
            {
                g.DrawImage(markImage, left, centre - mark / 2f, mark, mark);
            }

            var textLeft = left + mark + gap;
            var blockHeight = wordmarkSize.Height + 22f + line1Size.Height + 8f + line2Size.Height;
            var top = centre - blockHeight / 2f;

            using var wordmarkBrush = new SolidBrush(Color.FromArgb(255, 244, 246, 248));
            using var taglineBrush = new SolidBrush(Color.FromArgb(255, 150, 156, 166));

            g.DrawString(wordmark, wordmarkFont, wordmarkBrush,
                new PointF(textLeft, top), format);
            g.DrawString(line1, taglineFont, taglineBrush,
                new PointF(textLeft, top + wordmarkSize.Height + 22f), format);
            g.DrawString(line2, taglineFont, taglineBrush,
                new PointF(textLeft, top + wordmarkSize.Height + 22f + line1Size.Height + 8f), format);
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
