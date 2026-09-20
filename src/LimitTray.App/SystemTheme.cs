using System;
using Microsoft.Win32;

namespace LimitTray.App;

/// <summary>
/// Reads Windows' "apps use light theme" switch and raises Changed when it flips.
/// Missing key or denied read counts as dark, which is what the app looked like before.
/// </summary>
public static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event EventHandler? Changed;

    static SystemTheme() =>
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) Changed?.Invoke(null, EventArgs.Empty);
        };

    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception) { return false; }
    }
}
