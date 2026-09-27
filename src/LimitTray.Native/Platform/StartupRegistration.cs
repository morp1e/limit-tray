using Microsoft.Win32;

namespace LimitTray.Native.Platform;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LimitTray";

    public static bool IsSupported => !string.IsNullOrWhiteSpace(Environment.ProcessPath);

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (IsRegistryFailure(ex)) { return false; }
    }

    public static bool SetEnabled(bool enabled, IReadOnlyList<string> arguments)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;
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
        catch (Exception ex) when (IsRegistryFailure(ex)) { return false; }
    }

    public static bool PointsToCurrentExecutable()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key?.GetValue(ValueName) is not string value || value.Length == 0) return false;
            return MatchesExecutable(value, current);
        }
        catch (Exception ex) when (IsRegistryFailure(ex)) { return false; }
    }

    public static string CommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var carried = arguments
            .Where(a => a.StartsWith("--lang", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("tr", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var command = "\"" + executable + "\"";
        return carried.Length == 0 ? command : command + " " + string.Join(' ', carried);
    }

    internal static bool MatchesExecutable(string command, string executable)
    {
        try
        {
            return string.Equals(ExecutableFromCommand(command), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static string ExecutableFromCommand(string command)
    {
        var trimmed = command.TrimStart();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : string.Empty;
        }
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static bool IsRegistryFailure(Exception ex) => ex is System.Security.SecurityException
        or UnauthorizedAccessException or IOException;
}
