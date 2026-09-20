using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitTray.App;

/// <summary>
/// Acrylic behind a frameless WPF window, plus the two DWM attributes that make such a
/// window look native on Windows 11: rounded corners and no border line.
///
/// The Windows 11 "system backdrop" attribute was tried first and does not compose
/// reliably with a WPF window (it needs the frame extended into the client area, which
/// paints a light border and squares the corners). The composition attribute below is
/// the older, undocumented but widely used route and works from Windows 10 1803 on.
/// Every failure is silent: the effect is decoration and never worth an error.
/// </summary>
public static class WindowBackdrop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const uint DwmwaColorNone = 0xFFFFFFFE;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Rounded corners and no DWM border line. Windows 10 ignores both harmlessly.</summary>
    public static void ApplyNativeShape(Window window, bool dark)
    {
        var hwnd = Handle(window);
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

    /// <summary>
    /// Blurs whatever is behind the window and tints it. The tint alpha is kept low
    /// because the real surface colour comes from the window's own semi-transparent
    /// background on top; the accent only has to keep the blur from going black.
    /// </summary>
    public static bool TryApplyAcrylic(Window window, System.Windows.Media.Color tint)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source is null) return false;
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 2,
                GradientColor = (uint)(0x40 << 24) | (uint)(tint.B << 16) | (uint)(tint.G << 8) | tint.R,
            };
            return SetAccent(hwnd, accent);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Clear(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        try
        {
            SetAccent(hwnd, new AccentPolicy { AccentState = AccentDisabled });
        }
        catch (Exception)
        {
            // Nothing to undo if it never applied.
        }
    }

    private static bool SetAccent(IntPtr hwnd, AccentPolicy accent)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = pointer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;
}
