namespace LimitTray.Native.Graphics;

/// <summary>
/// CPU blur and compositing on premultiplied BGRA pixels. This replaces WPF's
/// DropShadowEffect for the panel shadow and the coloured glows. It runs only while the
/// popup is visible and only over the rectangle that holds the effect's source, so a
/// frame costs a few milliseconds and nothing when the popup is closed.
/// </summary>
internal static unsafe class Blur
{
    /// <summary>
    /// Gaussian-like blur of the given pixel rectangle by three box passes each way.
    /// WPF's BlurRadius r is treated as a Gaussian with sigma = r / 3.
    /// </summary>
    public static void Gaussian(Surface surface, int x0, int y0, int x1, int y1, float blurRadiusPx)
    {
        x0 = Math.Clamp(x0, 0, surface.Width);
        x1 = Math.Clamp(x1, 0, surface.Width);
        y0 = Math.Clamp(y0, 0, surface.Height);
        y1 = Math.Clamp(y1, 0, surface.Height);
        if (x1 - x0 < 2 || y1 - y0 < 2 || blurRadiusPx < 0.5f) return;

        var sigma = blurRadiusPx / 3f;
        // Three box passes of width w approximate a Gaussian with variance 3 * (w*w - 1) / 12.
        var w = (int)Math.Round(Math.Sqrt(4 * sigma * sigma + 1));
        var radius = Math.Max(1, w / 2);

        var width = x1 - x0;
        var height = y1 - y0;
        var scratch = new uint[Math.Max(width, height)];
        for (var pass = 0; pass < 3; pass++)
        {
            for (var y = y0; y < y1; y++)
                BoxLine(surface.Bits + y * surface.Width + x0, 1, width, radius, scratch);
            for (var x = x0; x < x1; x++)
                BoxLine(surface.Bits + y0 * surface.Width + x, surface.Width, height, radius, scratch);
        }
    }

    /// <summary>One running-sum box pass along a line of pixels; edges clamp to transparent.</summary>
    private static void BoxLine(uint* start, int stride, int length, int radius, uint[] scratch)
    {
        for (var i = 0; i < length; i++) scratch[i] = start[i * stride];

        var window = 2 * radius + 1;
        int sa = 0, sr = 0, sg = 0, sb = 0;
        for (var i = 0; i < radius && i < length; i++) Add(scratch[i], ref sa, ref sr, ref sg, ref sb, 1);

        for (var i = 0; i < length; i++)
        {
            var enter = i + radius;
            if (enter < length) Add(scratch[enter], ref sa, ref sr, ref sg, ref sb, 1);
            var leave = i - radius - 1;
            if (leave >= 0) Add(scratch[leave], ref sa, ref sr, ref sg, ref sb, -1);

            start[i * stride] =
                (uint)(sa / window) << 24 | (uint)(sr / window) << 16 |
                (uint)(sg / window) << 8 | (uint)(sb / window);
        }
    }

    private static void Add(uint p, ref int a, ref int r, ref int g, ref int b, int sign)
    {
        a += sign * (int)(p >> 24);
        r += sign * (int)((p >> 16) & 0xFF);
        g += sign * (int)((p >> 8) & 0xFF);
        b += sign * (int)(p & 0xFF);
    }

    /// <summary>Premultiplied source-over of <paramref name="src"/> onto <paramref name="dst"/> in a rectangle.</summary>
    public static void CompositeOver(Surface dst, Surface src, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, Math.Min(dst.Width, src.Width));
        x1 = Math.Clamp(x1, 0, Math.Min(dst.Width, src.Width));
        y0 = Math.Clamp(y0, 0, Math.Min(dst.Height, src.Height));
        y1 = Math.Clamp(y1, 0, Math.Min(dst.Height, src.Height));

        for (var y = y0; y < y1; y++)
        {
            var d = dst.Bits + y * dst.Width;
            var s = src.Bits + y * src.Width;
            for (var x = x0; x < x1; x++)
            {
                var sp = s[x];
                var sa = sp >> 24;
                if (sa == 0) continue;
                if (sa == 255) { d[x] = sp; continue; }

                var dp = d[x];
                var inv = 255 - sa;
                var a = sa + Mul(dp >> 24, inv);
                var r = ((sp >> 16) & 0xFF) + Mul((dp >> 16) & 0xFF, inv);
                var g = ((sp >> 8) & 0xFF) + Mul((dp >> 8) & 0xFF, inv);
                var b = (sp & 0xFF) + Mul(dp & 0xFF, inv);
                d[x] = Math.Min(a, 255u) << 24 | Math.Min(r, 255u) << 16 | Math.Min(g, 255u) << 8 | Math.Min(b, 255u);
            }
        }
    }

    private static uint Mul(uint c, uint a) => (c * a + 127) / 255;
}
