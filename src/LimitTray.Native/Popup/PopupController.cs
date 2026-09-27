using LimitTray.Native.Host;
using LimitTray.Native.Ui;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Popup;

/// <summary>
/// Owns the popup while it is open and nothing while it is closed. Opening builds the
/// window, the Direct2D/DirectWrite factories, the surfaces and the page state; closing
/// disposes all of it. v0.3 built the popup at start-up and only hid it, which kept the
/// WPF visual tree and its render resources alive for the life of the process.
/// </summary>
internal sealed unsafe class PopupController : IPopupController, IPopupInput
{
    private const nuint AnimationTimer = 1;
    private const nuint ClockTimer = 2;
    private const nuint ExpiryTimer = 3;
    private const uint FrameMilliseconds = 16;
    private const int VirtualKeyEscape = 0x1B;

    private readonly IAppActions _app;
    private PopupWindow? _window;
    private PopupRenderer? _renderer;
    private PopupContent? _content;
    private RECT _workArea;
    private bool _animating;

    public PopupController(IAppActions app) => _app = app;

    /// <summary>Raised when the graphics stack cannot be created; the host tells the user.</summary>
    public event Action? GraphicsFailed;

    public bool IsOpen => _window is not null;

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        // Clicking the tray icon while the popup is open first takes focus from the popup,
        // which closes it, and then delivers the click, which would open it again. A toggle
        // right after a focus-loss close is that same click; it means "close".
        if (Environment.TickCount64 - _deactivatedAt < ReopenGuardMilliseconds) return;
        Open();
    }

    private const long ReopenGuardMilliseconds = 300;
    private long _deactivatedAt = long.MinValue / 2;

    /// <summary>From the tray menu: opens if needed, then slides to settings, as v0.3 did.</summary>
    public void OpenSettings()
    {
        if (!IsOpen) Open();
        if (_content is null) return;
        _content.ShowSettings(Environment.TickCount64);
        Render();
    }

    public void Close()
    {
        if (_window is null) return;
        _window.KillTimer(AnimationTimer);
        _window.KillTimer(ClockTimer);
        _window.KillTimer(ExpiryTimer);
        _window.Dispose();
        _renderer?.Dispose();
        _window = null;
        _renderer = null;
        _content = null;
        _animating = false;
    }

    public void OnDataChanged()
    {
        if (_content is null) return;
        Refresh();
        Render();
    }

    private void Open()
    {
        var scale = MonitorUnderCursor(out _workArea);
        try
        {
            _renderer = new PopupRenderer(scale);
        }
        catch (Exception)
        {
            // No Direct2D or DirectWrite: the tray keeps working and the user is told.
            GraphicsFailed?.Invoke();
            return;
        }

        var animations = ClientAreaAnimation();
        _content = new PopupContent(
            new PanelPage { AnimationsEnabled = animations },
            new SettingsPage { AnimationsEnabled = animations });
        Refresh();
        _window = new PopupWindow(this);
        Render();
        _window.Show();
        ScheduleClock();
    }

    /// <summary>Pushes the host's current state into both pages.</summary>
    private void Refresh()
    {
        if (_content is null) return;
        var palette = _app.IsDarkTheme ? Palette.DarkTheme : Palette.LightTheme;
        _content.Panel.Palette = palette;
        _content.Panel.Strings = _app.Strings;
        _content.Panel.Update(_app.Snapshots, _app.History, _app.Settings, DateTimeOffset.Now, Environment.TickCount64);
        _content.Settings.Palette = palette;
        _content.Settings.Strings = _app.Strings;
        _content.Settings.Update(_app.Settings, _app.StartupEnabled, _app.SettingsSaveFailed);
    }

    private void Render()
    {
        if (_window is null || _renderer is null || _content is null) return;
        var now = Environment.TickCount64;
        var surface = _renderer.Render(_content, now);

        // Bottom-right of the work area of the monitor the tray was clicked on, 12 DIPs
        // in, as v0.3; the window grows upwards because its bottom edge is the anchor.
        var inset = (int)MathF.Round(12 * _renderer.Scale);
        _window.Present(surface, _workArea.right - inset - surface.Width, _workArea.bottom - inset - surface.Height);

        var animating = _content.IsAnimating(now);
        if (animating && !_animating) _window.SetTimer(AnimationTimer, FrameMilliseconds);
        if (!animating && _animating) _window.KillTimer(AnimationTimer);
        _animating = animating;

        // A transient message (terminal failure) needs one redraw when it expires.
        if (_content.Panel.NextExpiry(now) is { } expiry)
            _window.SetTimer(ExpiryTimer, (uint)Math.Max(1, expiry - now + 10));
    }

    /// <summary>Age and countdown texts change on minute boundaries; one wake-up per minute while open.</summary>
    private void ScheduleClock()
    {
        if (_window is null) return;
        var now = DateTimeOffset.Now;
        var untilNextMinute = 60_000 - (now.Second * 1000 + now.Millisecond) + 50;
        _window.SetTimer(ClockTimer, (uint)untilNextMinute);
    }

    // ---- input ------------------------------------------------------------------

    private Hit HitAt(int x, int y, out bool onSettings)
    {
        onSettings = false;
        if (_renderer is null || _content is null) return Hit.None;
        var point = _renderer.ToPanel(x, y);
        if (_content.IsSliding(Environment.TickCount64)) return Hit.None;
        onSettings = _content.SettingsShown;
        return onSettings ? _content.Settings.HitTest(point.X, point.Y) : _content.Panel.HitTest(point.X, point.Y);
    }

    public void OnMouseMove(int x, int y)
    {
        if (_content is null) return;
        var hit = HitAt(x, y, out var onSettings);
        bool changed;
        if (onSettings)
        {
            var key = hit.Kind == HitKind.None ? null : hit.Key;
            var item = hit.Kind is HitKind.Control or HitKind.DropdownItem ? hit.Index : -1;
            changed = key != _content.Settings.Hovered || item != _content.Settings.HoveredItem;
            _content.Settings.Hovered = key;
            _content.Settings.HoveredItem = item;
        }
        else
        {
            var provider = hit.Kind is HitKind.Card or HitKind.Terminal ? hit.Key : null;
            changed = provider != _content.Panel.Hovered || hit.Kind != _content.Panel.HoveredKind;
            _content.Panel.Hovered = provider;
            _content.Panel.HoveredKind = hit.Kind;
        }
        if (changed) Render();
    }

    public void OnMouseLeave()
    {
        if (_content is null) return;
        _content.Panel.Hovered = null;
        _content.Panel.HoveredKind = HitKind.None;
        _content.Settings.Hovered = null;
        _content.Settings.HoveredItem = -1;
        Render();
    }

    public void OnMouseUp(int x, int y)
    {
        if (_content is null) return;
        var hit = HitAt(x, y, out var onSettings);
        var now = Environment.TickCount64;
        if (onSettings) OnSettingsClick(hit, now);
        else OnPanelClick(hit, now);
        Render();
    }

    private void OnPanelClick(Hit hit, long now)
    {
        var panel = _content!.Panel;
        switch (hit.Kind)
        {
            case HitKind.Card:
                panel.ToggleExpanded(hit.Key!);
                _app.RememberExpanded(panel.Expanded);
                break;
            case HitKind.Terminal:
                if (!_app.OpenTerminal(hit.Key!)) panel.ShowTerminalError(hit.Key!, now + 3000);
                break;
            case HitKind.Refresh:
                panel.StartRefreshTurn(now);
                _app.RefreshNow();
                break;
            case HitKind.Settings:
                _content.ShowSettings(now);
                break;
        }
    }

    private void OnSettingsClick(Hit hit, long now)
    {
        var page = _content!.Settings;
        switch (hit.Kind)
        {
            case HitKind.Back:
                _content.ShowPanel(now);
                break;
            case HitKind.GitHub:
                _app.OpenRepository();
                break;
            case HitKind.DropdownItem:
                page.CloseList();
                Apply(page.Choose(hit.Key!, hit.Index));
                break;
            case HitKind.Control when hit.Key == "startup":
                _app.SetStartup(!_app.StartupEnabled);
                Refresh();
                break;
            case HitKind.Control when hit.Key is "refresh" or "caution" or "warning" or "tray-source":
                if (page.OpenDropdown == hit.Key) page.CloseList();
                else page.OpenList(hit.Key!, now);
                break;
            case HitKind.Control:
                Apply(page.Choose(hit.Key!, hit.Index));
                break;
            default:
                // A click outside an open list closes it and does nothing else.
                page.CloseList();
                break;
        }
    }

    private void Apply(Core.Settings.AppSettings? next)
    {
        if (next is null || next.Equals(_app.Settings)) return;
        // The host saves, applies and calls OnDataChanged, which refreshes both pages.
        _app.ApplySettings(next);
    }

    public void OnKey(int virtualKey)
    {
        if (virtualKey != VirtualKeyEscape || _content is null) return;
        if (_content.Settings.OpenDropdown is not null)
        {
            _content.Settings.CloseList();
            Render();
            return;
        }
        Close();
    }

    public void OnDeactivated()
    {
        if (!IsOpen) return;
        _deactivatedAt = Environment.TickCount64;
        Close();
    }

    public void OnTimer(nuint id)
    {
        if (id == ClockTimer)
        {
            Refresh();
            Render();
            ScheduleClock();
            return;
        }
        if (id == ExpiryTimer) _window?.KillTimer(ExpiryTimer);
        Render();
    }

    public void OnDpiChanged(float scale)
    {
        if (_renderer is null) return;
        _renderer.SetScale(scale);
        Render();
    }

    // ---- platform -----------------------------------------------------------------

    private static float MonitorUnderCursor(out RECT workArea)
    {
        PInvoke.GetCursorPos(out var cursor);
        var monitor = PInvoke.MonitorFromPoint(cursor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &info);
        workArea = info.rcWork;
        uint dpiX = 96, dpiY = 96;
        // The analyser reads CsWin32's "windows8.1" as version 8.1; every supported Windows is 10.0+.
        if (OperatingSystem.IsWindowsVersionAtLeast(8, 1))
            PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpiX, &dpiY);
        return dpiX / 96f;
    }

    /// <summary>SPI_GETCLIENTAREAANIMATION: when off, v0.3 ran every animation with zero duration.</summary>
    private static bool ClientAreaAnimation()
    {
        BOOL enabled = true;
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0);
        return enabled;
    }
}
