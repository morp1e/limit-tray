using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimitTray.App;

/// <summary>
/// Acrylic behind a frameless WPF window, plus the two DWM attributes that make such a
/// window look native on Windows 11: rounded corners and no border line.
///
/// On Windows 11 22H2+ the documented system backdrop is used, with the frame extended
/// into the client area and its side effects (light border, square corners) turned off
/// through DWM attributes. Windows 10 falls back to the older composition attribute.
/// Every failure is silent: the effect is decoration and never worth an error.
/// </summary>
public static class WindowBackdrop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropTransient = 3; // acrylic
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>Windows 11 22H2 introduced the system backdrop attribute.</summary>
    public static bool HasSystemBackdrop => Environment.OSVersion.Version.Build >= 22621;

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

            // Windows 11 22H2+: the documented backdrop. It needs the DWM frame extended
            // over the whole client area so the backdrop has somewhere to draw; the light
            // border line and square corners that come with that are switched off in
            // ApplyNativeShape. Measured 2026-09-20: the composition-attribute route below
            // no longer blurs on build 26200, so it is only the fallback for Windows 10.
            if (HasSystemBackdrop)
            {
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) return false;
                var backdrop = BackdropTransient;
                return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
            }

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
            if (HasSystemBackdrop)
            {
                var none = BackdropNone;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
                var margins = new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
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
