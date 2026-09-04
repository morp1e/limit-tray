using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace LimitTray.App;

/// <summary>
/// Reads and writes the per-user "run at login" entry.
///
/// Only ever touched when the user picks the menu item: a quota monitor that installs
/// itself into startup without being asked is not a monitor, it is an infestation.
/// Every registry call is wrapped, because the key can be locked down by policy and a
/// failure to read it is not a reason to fail to start.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LimitTray";

    public static bool IsSupported => ExecutablePath() is not null;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                      or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Returns true when the registry now matches what was asked for.</summary>
    public static bool SetEnabled(bool enabled, IReadOnlyList<string> arguments)
    {
        var executable = ExecutablePath();
        if (executable is null) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            key.SetValue(ValueName, CommandLine(executable, arguments), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                      or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The language override is carried into the startup command so the app does not
    /// silently change language the first time Windows launches it.
    /// </summary>
    internal static string CommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var carried = arguments
            .Where(a => a.StartsWith("--lang", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("tr", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var command = "\"" + executable + "\"";
        return carried.Count == 0 ? command : command + " " + string.Join(' ', carried);
    }

    private static string? ExecutablePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
