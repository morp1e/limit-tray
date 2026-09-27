using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using LimitTray.Native.Host;

namespace LimitTray.Native.Tray;

internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint IconId = 1;
    private readonly MessageWindow _window;
    private HICON _icon;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    public TrayIcon(MessageWindow window, HICON icon, string tooltip)
    {
        _window = window;
        _icon = icon;
        _tooltip = Truncate(tooltip);
        _window.Register(CallbackMessage, OnCallback);
        _window.Register(window.TaskbarCreatedMessage, (_, _) =>
        {
            Add();
            return new LRESULT(0);
        });
        Add();
    }

    public event Action? LeftClick;
    public event Action<int, int>? RightClick;

    public void SetIcon(HICON icon)
    {
        _icon = icon;
        if (_added) Modify();
    }

    public void SetTooltip(string text)
    {
        _tooltip = Truncate(text);
        if (_added) Modify();
    }

    public void ShowBalloon(string title, string body)
    {
        if (!_added) return;
        var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING;
        title.AsSpan(0, Math.Min(title.Length, 63)).CopyTo(data.szInfoTitle.AsSpan());
        body.AsSpan(0, Math.Min(body.Length, 255)).CopyTo(data.szInfo.AsSpan());
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data)) _added = false;
    }

    public RECT? GetIconRect()
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            hWnd = _window.Handle,
            uID = IconId,
        };
        RECT rect;
        var result = PInvoke.Shell_NotifyIconGetRect(&identifier, &rect);
        return result.Succeeded ? rect : null;
    }

    private void Add()
    {
        var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                              NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        var pointer = &data;
        {
            if (PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, pointer))
            {
                data.uVersion = 4;
                PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, pointer);
                _added = true;
            }
            else
            {
                _added = false;
            }
        }
    }

    /// <summary>
    /// A failed modify means Explorer is gone or restarting. The icon is marked as not
    /// added and comes back on TaskbarCreated; throwing here would end the process.
    /// </summary>
    private void Modify()
    {
        var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP |
                              NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data)) _added = false;
    }

    private NOTIFYICONDATAW CreateData(NOTIFY_ICON_DATA_FLAGS flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        _tooltip.AsSpan().CopyTo(data.szTip.AsSpan());
        return data;
    }

    private LRESULT? OnCallback(WPARAM wParam, LPARAM lParam)
    {
        var notification = (uint)(lParam.Value & 0xffff);
        if (notification == PInvoke.WM_LBUTTONUP)
        {
            LeftClick?.Invoke();
        }
        else if (notification == PInvoke.WM_CONTEXTMENU)
        {
            var position = PInvoke.GetMessagePos();
            RightClick?.Invoke((short)(position & 0xffff), (short)((position >> 16) & 0xffff));
        }

        return new LRESULT(0);
    }

    private static string Truncate(string text) => text.Length <= 127 ? text : text[..127];

    private static void Check(BOOL result, string operation)
    {
        if (!result) throw new InvalidOperationException(operation + " failed: " + Marshal.GetLastPInvokeError());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var data = CreateData((NOTIFY_ICON_DATA_FLAGS)0);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _added = false;
        }
    }
}
