using System;
using System.ComponentModel;
using System.Diagnostics;

namespace LimitTray.App;

public static class TerminalLauncher
{
    public static bool Open(string provider)
    {
        var command = provider switch { "claude" => "claude", "codex" => "codex", _ => null };
        if (command is null) return false;

        return TryStart("wt.exe", $"new-tab {command}")
            || TryStart("powershell.exe", $"-NoExit -Command {command}");
    }

    private static bool TryStart(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
