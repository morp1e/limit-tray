using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LimitTray.Native.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Popup;

/// <summary>Receives the popup window's input; implemented by <see cref="PopupController"/>.</summary>
internal interface IPopupInput
{
    void OnMouseMove(int x, int y);
    void OnMouseLeave();
    void OnMouseUp(int x, int y);
    void OnKey(int virtualKey);
    void OnDeactivated();
    void OnTimer(nuint id);
    void OnDpiChanged(float scale);
}

/// <summary>
/// The per-pixel transparent popup. Its content, size and position are set in one
/// UpdateLayeredWindow call, so a height change never shows a stale frame (the ghost
/// that v0.3.3 fought with a fixed 392x700 window). No DWM attributes are applied: on
/// a layered window they made DWM paint a tinted rectangle on one display in v0.3.4.
/// The window is created on open and destroyed on close.
/// </summary>
internal sealed unsafe class PopupWindow : IDisposable
{
    public const string ClassName = "LimitTray.Popup";
    private static readonly Dictionary<nint, PopupWindow> Windows = new();
    private static bool _registered;

    private readonly IPopupInput _input;
    private bool _tracking;
    private bool _disposed;

    public PopupWindow(IPopupInput input)
    {
        _input = input;
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        fixed (char* className = ClassName)
        {
            if (!_registered)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WindowProc,
                    hInstance = instance,
                    lpszClassName = className,
                    hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                };
                if (PInvoke.RegisterClassEx(&windowClass) == 0)
                    throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
                _registered = true;
            }

            Handle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST,
                className, null, WINDOW_STYLE.WS_POPUP, 0, 0, 1, 1, HWND.Null, HMENU.Null, instance, null);
        }

        if (Handle == HWND.Null)
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        Windows[(nint)Handle.Value] = this;
    }

    public HWND Handle { get; }

    /// <summary>Puts the frame on screen at (x, y) with the surface's size, atomically.</summary>
    public void Present(Surface surface, int x, int y)
    {
        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            var destination = new System.Drawing.Point(x, y);
            var size = new SIZE { cx = surface.Width, cy = surface.Height };
            var source = new System.Drawing.Point(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            PInvoke.UpdateLayeredWindow(Handle, screen, &destination, &size, surface.Dc, &source,
                new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    public void Show()
    {
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetForegroundWindow(Handle);
    }

    public void SetTimer(nuint id, uint milliseconds) => PInvoke.SetTimer(Handle, id, milliseconds, null);

    public void KillTimer(nuint id) => PInvoke.KillTimer(Handle, id);

    private LRESULT? Dispatch(uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var track = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                        dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
                        hwndTrack = Handle,
                    };
                    PInvoke.TrackMouseEvent(&track);
                    _tracking = true;
                }
                _input.OnMouseMove(X(lParam), Y(lParam));
                return new LRESULT(0);
            case PInvoke.WM_MOUSELEAVE:
                _tracking = false;
                _input.OnMouseLeave();
                return new LRESULT(0);
            case PInvoke.WM_LBUTTONUP:
                _input.OnMouseUp(X(lParam), Y(lParam));
                return new LRESULT(0);
            case PInvoke.WM_KEYDOWN:
                _input.OnKey((int)wParam.Value);
                return new LRESULT(0);
            case PInvoke.WM_ACTIVATE:
                if ((wParam.Value & 0xFFFF) == 0) _input.OnDeactivated();
                return new LRESULT(0);
            case PInvoke.WM_TIMER:
                _input.OnTimer(wParam.Value);
                return new LRESULT(0);
            case PInvoke.WM_DPICHANGED:
                _input.OnDpiChanged((wParam.Value & 0xFFFF) / 96f);
                return new LRESULT(0);
        }
        return null;
    }

    private static int X(LPARAM lParam) => (short)(lParam.Value & 0xFFFF);
    private static int Y(LPARAM lParam) => (short)((lParam.Value >> 16) & 0xFFFF);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Windows.Remove((nint)Handle.Value);
        PInvoke.DestroyWindow(Handle);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (Windows.TryGetValue((nint)hwnd.Value, out var window))
        {
            var result = window.Dispatch(message, wParam, lParam);
            if (result.HasValue) return result.Value;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
