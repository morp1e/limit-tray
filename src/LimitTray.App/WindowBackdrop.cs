using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitTray.App;

/// <summary>
/// Windows 11 22H2+ system backdrop through DWM. Anything older, or a DWM that refuses,
/// silently keeps the flat surface: the effect is decoration and never worth an error.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropTransient = 3; // acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool TryApplyAcrylic(Window window, bool dark)
    {
        try
        {
            if (!IsSupported) return false;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            var darkValue = dark ? 1 : 0;
            var darkResult = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode,
                ref darkValue, sizeof(int));
            if (darkResult != 0) return false;

            var backdrop = BackdropTransient;
            var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            if (result != 0) return false;

            // The backdrop shows only through a transparent client area.
            HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Clear(Window window)
    {
        if (!IsSupported) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var none = BackdropNone;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
    }
}
