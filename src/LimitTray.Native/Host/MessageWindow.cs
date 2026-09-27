using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Host;

internal sealed unsafe class MessageWindow : IDisposable
{
    public const string ClassName = "LimitTray.Host";
    private static readonly Dictionary<nint, MessageWindow> Windows = new();
    private readonly Dictionary<uint, Func<WPARAM, LPARAM, LRESULT?>> _handlers = new();
    private bool _disposed;

    public MessageWindow()
    {
        var instance = PInvoke.GetModuleHandle((PCWSTR)null);
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = (HINSTANCE)(nint)instance.Value,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(&windowClass) == 0)
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());

            // TaskbarCreated is broadcast only to top-level windows; HWND_MESSAGE would miss Explorer restarts.
            Handle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0,
                HWND.Null,
                HMENU.Null,
                (HINSTANCE)(nint)instance.Value,
                null);
        }

        if (Handle == HWND.Null)
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());

        Windows.Add((nint)Handle.Value, this);
        TaskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
    }

    public HWND Handle { get; }
    public uint TaskbarCreatedMessage { get; }

    public void Register(uint message, Func<WPARAM, LPARAM, LRESULT?> handler) => _handlers[message] = handler;

    internal LRESULT? Dispatch(uint message, WPARAM wParam, LPARAM lParam) =>
        _handlers.TryGetValue(message, out var handler) ? handler(wParam, lParam) : null;

    public static int RunLoop()
    {
        MSG message;
        BOOL result;
        while ((result = PInvoke.GetMessage(&message, HWND.Null, 0, 0)).Value > 0)
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }

        return result.Value < 0 ? 1 : (int)message.wParam.Value;
    }

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
