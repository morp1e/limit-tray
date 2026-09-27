using LimitTray.Core.Presentation;
using LimitTray.Native.Host;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Tray;

internal sealed unsafe class TrayMenu
{
    private const int StartupCommand = 1;
    private const int SettingsCommand = 2;
    private const int ExitCommand = 3;
    private readonly MessageWindow _window;
    private readonly Func<Strings> _strings;
    private readonly Func<bool> _startupEnabled;
    private readonly Action<bool> _setStartup;
    private readonly Action _openSettings;
    private readonly Action _exit;

    public TrayMenu(MessageWindow window, Func<Strings> strings, Func<bool> startupEnabled,
        Action<bool> setStartup, Action openSettings, Action exit)
    {
        _window = window;
        _strings = strings;
        _startupEnabled = startupEnabled;
        _setStartup = setStartup;
        _openSettings = openSettings;
        _exit = exit;
    }

    public void Show(int x, int y)
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu == HMENU.Null) return;
        try
        {
            var strings = _strings();
            var startupFlags = MENU_ITEM_FLAGS.MF_STRING;
            if (_startupEnabled()) startupFlags |= MENU_ITEM_FLAGS.MF_CHECKED;
            Append(menu, startupFlags, (nuint)StartupCommand, strings.StartWithWindows);
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)SettingsCommand, strings.Settings);
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)ExitCommand, strings.Exit);
            PInvoke.SetForegroundWindow(_window.Handle);
            var command = PInvoke.TrackPopupMenu(menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON,
                x, y, 0, _window.Handle, null);
            switch ((int)command.Value)
            {
                case StartupCommand: _setStartup(!_startupEnabled()); break;
                case SettingsCommand: _openSettings(); break;
                case ExitCommand: _exit(); break;
            }
        }
        finally { PInvoke.DestroyMenu(menu); }
    }

    private static void Append(HMENU menu, MENU_ITEM_FLAGS flags, nuint id, string text)
    {
        fixed (char* label = text) PInvoke.AppendMenu(menu, flags, (nuint)id, label);
    }
}
