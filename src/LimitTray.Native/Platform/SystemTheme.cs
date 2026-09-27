using Microsoft.Win32;

namespace LimitTray.Native.Platform;

internal static class SystemTheme
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Missing key or denied read counts as dark, preserving the pre-native appearance.</summary>
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 1;
        }
        catch (Exception) { return true; }
    }
}
