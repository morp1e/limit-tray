using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LimitTray.App;

/// <summary>
/// An <see cref="Icon"/> together with the unmanaged handle it was built from.
///
/// <c>Bitmap.GetHicon</c> creates an icon handle that the caller owns, and
/// <c>Icon.FromHandle</c> deliberately does not take ownership of it: disposing that
/// Icon frees nothing. A tray application redraws its icon every time the quota moves,
/// so the handles pile up for as long as the process runs. This type owns the handle
/// and destroys it, which is the only way the leak actually closes.
/// </summary>
public sealed class RenderedIcon : IDisposable
{
    // DllImport rather than the LibraryImport source generator: the generated marshalling
    // code requires AllowUnsafeBlocks, and turning unsafe code on for the whole project
    // to free one handle is a poor trade.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private IntPtr _handle;

    internal RenderedIcon(IntPtr handle)
    {
        _handle = handle;
        Icon = Icon.FromHandle(handle);
    }

    public Icon Icon { get; }

    public void Dispose()
    {
        Icon.Dispose();

        if (_handle == IntPtr.Zero) return;
        DestroyIcon(_handle);
        _handle = IntPtr.Zero;
    }
}
