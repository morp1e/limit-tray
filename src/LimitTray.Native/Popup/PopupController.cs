using LimitTray.Native.Graphics;
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
    private const uint FrameMilliseconds = 16;

    private readonly IAppActions _app;
    private PopupWindow? _window;
    private PopupRenderer? _renderer;
    private PanelPage? _panel;
    private RECT _workArea;
    private bool _animating;

    public PopupController(IAppActions app) => _app = app;

    /// <summary>Raised once when the graphics stack cannot be created; the host tells the user.</summary>
    public event Action? GraphicsFailed;

    public bool IsOpen => _window is not null;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void OpenSettings()
    {
        if (!IsOpen) Open();
        // The settings page arrives with Task 13; until then the panel opens.
    }

    public void Close()
    {
        if (_window is null) return;
        if (_panel is not null) _app.RememberExpanded(_panel.Expanded);
        _window.KillTimer(AnimationTimer);
        _window.KillTimer(ClockTimer);
        _window.Dispose();
        _renderer?.Dispose();
        _window = null;
        _renderer = null;
        _panel = null;
        _animating = false;
    }

    public void OnDataChanged()
    {
        if (_panel is null) return;
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
            // No Direct2D or DirectWrite: the tray keeps working and the user is told once.
            GraphicsFailed?.Invoke();
            return;
        }

        _panel = new PanelPage { AnimationsEnabled = ClientAreaAnimation() };
        Refresh();
        _window = new PopupWindow(this);
        Render();
        _window.Show();
        ScheduleClock();
    }

    /// <summary>Pushes the host's current state into the page.</summary>
    private void Refresh()
    {
        if (_panel is null) return;
        _panel.Palette = _app.IsDarkTheme ? Palette.DarkTheme : Palette.LightTheme;
        _panel.Strings = _app.Strings;
        _panel.Update(_app.Snapshots, _app.History, _app.Settings, DateTimeOffset.Now, Environment.TickCount64);
    }

    private void Render()
    {
        if (_window is null || _renderer is null || _panel is null) return;
        var now = Environment.TickCount64;
        var surface = _renderer.Render(_panel, now);

        // Bottom-right of the work area of the monitor the tray was clicked on, 12 DIPs
        // in, as v0.3; the window grows upwards because its bottom edge is the anchor.
        var inset = (int)MathF.Round(12 * _renderer.Scale);
        var x = _workArea.right - inset - surface.Width;
        var y = _workArea.bottom - inset - surface.Height;
        _window.Present(surface, x, y);

        var animating = _panel.IsAnimating(now);
        if (animating && !_animating) _window.SetTimer(AnimationTimer, FrameMilliseconds);
        if (!animating && _animating) _window.KillTimer(AnimationTimer);
        _animating = animating;
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

    public void OnMouseMove(int x, int y)
    {
        if (_renderer is null || _panel is null) return;
        var point = _renderer.ToPanel(x, y);
        var hit = _panel.HitTest(point.X, point.Y);
        var hovered = hit.Kind is HitKind.Card or HitKind.Terminal ? hit.Key : null;
        if (hovered == _panel.Hovered && hit.Kind == _panel.HoveredKind) return;
        _panel.Hovered = hovered;
        _panel.HoveredKind = hit.Kind;
        Render();
    }

    public void OnMouseLeave()
    {
        if (_panel is null || (_panel.Hovered is null && _panel.HoveredKind == HitKind.None)) return;
        _panel.Hovered = null;
        _panel.HoveredKind = HitKind.None;
        Render();
    }

    public void OnMouseUp(int x, int y)
    {
        if (_renderer is null || _panel is null) return;
        var point = _renderer.ToPanel(x, y);
        var hit = _panel.HitTest(point.X, point.Y);
        var now = Environment.TickCount64;
        switch (hit.Kind)
        {
            case HitKind.Card:
                _panel.ToggleExpanded(hit.Key!);
                _app.RememberExpanded(_panel.Expanded);
                break;
            case HitKind.Terminal:
                if (!_app.OpenTerminal(hit.Key!)) _panel.ShowTerminalError(hit.Key!, now + 3000);
                break;
            case HitKind.Refresh:
                _panel.StartRefreshTurn(now);
                _app.RefreshNow();
                break;
            case HitKind.Settings:
                OpenSettings();
                break;
            default:
                return;
        }
        Render();
    }

    public void OnKey(int virtualKey)
    {
        if (virtualKey == 0x1B) Close(); // Esc
    }

    public void OnDeactivated() => Close();

    public void OnTimer(nuint id)
    {
        if (id == ClockTimer)
        {
            Refresh();
            Render();
            ScheduleClock();
            return;
        }
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
