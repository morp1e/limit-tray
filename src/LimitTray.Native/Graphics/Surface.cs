using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace LimitTray.Native.Graphics;

/// <summary>
/// A 32 bpp top-down premultiplied BGRA DIB section selected into its own memory DC.
/// It is what Direct2D draws into and what UpdateLayeredWindow and CreateIconIndirect
/// read. It exists only while something is being shown; dispose releases the pixels.
/// </summary>
internal sealed unsafe class Surface : IDisposable
{
    private HDC _dc;
    private HBITMAP _bitmap;
    private HGDIOBJ _previous;

    public Surface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            _dc = PInvoke.CreateCompatibleDC(screen);
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screen);
        }

        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = Width;
        info.bmiHeader.biHeight = -Height; // top-down
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        _bitmap = PInvoke.CreateDIBSection(_dc, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (_bitmap.IsNull || bits is null)
        {
            PInvoke.DeleteDC(_dc);
            throw new InvalidOperationException("CreateDIBSection failed");
        }

        Bits = (uint*)bits;
        _previous = PInvoke.SelectObject(_dc, (HGDIOBJ)(nint)_bitmap.Value);
    }

    public int Width { get; }
    public int Height { get; }
    public HDC Dc => _dc;
    public HBITMAP Bitmap => _bitmap;

    /// <summary>Premultiplied BGRA pixels, row-major, Width * Height.</summary>
    public uint* Bits { get; private set; }

    public Span<uint> Pixels => new(Bits, Width * Height);

    public void Clear() => Pixels.Clear();

    public void Dispose()
    {
        if (_dc.IsNull) return;
        PInvoke.SelectObject(_dc, _previous);
        PInvoke.DeleteObject((HGDIOBJ)(nint)_bitmap.Value);
        PInvoke.DeleteDC(_dc);
        _dc = HDC.Null;
        _bitmap = HBITMAP.Null;
        Bits = null;
    }
}
