using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LimitTray.Core.Presentation;
using LimitTray.Native.Host;
using LimitTray.Native.Tray;

namespace LimitTray.Native;

internal static unsafe class Program
{
    [STAThread]
    private static int Main()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "limit-tray");
        using var instance = SingleInstance.TryAcquire(dataDirectory);
        if (instance is null) return 0;

        using var window = new MessageWindow();
        UiThread.Initialize(window);
        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var loadedIcon = PInvoke.LoadImage(
            (HINSTANCE)(nint)module.Value,
            new PCWSTR((char*)32512),
            GDI_IMAGE_TYPE.IMAGE_ICON,
            0,
            0,
            IMAGE_FLAGS.LR_DEFAULTSIZE | IMAGE_FLAGS.LR_SHARED);
        var icon = (HICON)(nint)loadedIcon.Value;
        if (icon == HICON.Null)
            throw new InvalidOperationException("LoadImage failed: " + Marshal.GetLastPInvokeError());

        using var tray = new TrayIcon(window, icon, "Lim'it");
        tray.RightClick += (x, y) => ShowExitMenu(window, x, y);
        return MessageWindow.RunLoop();
    }

    private static void ShowExitMenu(MessageWindow window, int x, int y)
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu == HMENU.Null) return;

        try
        {
            var exit = Strings.ForCulture(CultureInfo.CurrentUICulture).Exit;
            fixed (char* text = exit)
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, 1, text);
            PInvoke.SetForegroundWindow(window.Handle);
            var command = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON,
                x,
                y,
                0,
                window.Handle,
                null);
            if (command.Value == 1) PInvoke.PostQuitMessage(0);
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }
}
