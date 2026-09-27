using LimitTray.Core.Presentation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Graphics;

/// <summary>
/// Turns the Core tray model into an icon. The caller owns the returned HICON and
/// destroys the previous one after the shell has taken the new one.
/// </summary>
internal interface ITrayIconPainter
{
    HICON Paint(TrayIconModel model, int sizePx);
}
