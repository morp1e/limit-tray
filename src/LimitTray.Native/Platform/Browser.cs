using System.Diagnostics;

namespace LimitTray.Native.Platform;

internal static class Browser
{
    public static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/morp1e/limit-tray") { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
