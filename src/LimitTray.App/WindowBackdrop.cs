using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitTray.App;

/// <summary>
/// The two DWM attributes that make a frameless WPF window look native on Windows 11:
/// rounded corners and no border line. Windows 10 ignores both harmlessly, and every
/// failure is silent because this is decoration and never worth an error.
///
/// An acrylic backdrop was tried here during v0.3 (both the composition attribute and
/// the Windows 11 system backdrop). The first did nothing on build 26200; the second
/// worked but the difference was not worth a setting. Both were removed.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const uint DwmwaColorNone = 0xFFFFFFFE;

    /// <summary>Windows 11 is where DWM rounds a frameless window's corners.</summary>
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    public static void ApplyNativeShape(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var darkValue = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int));
            var round = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
            var none = DwmwaColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(uint));
        }
        catch (Exception)
        {
            // Older DWM: the window simply keeps square corners.
        }
    }
}
