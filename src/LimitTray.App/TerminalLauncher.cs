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

        // The CLI is launched through PowerShell in both cases. Windows Terminal given the
        // bare command name resolves it with CreateProcess semantics, which was observed
        // to open and close a tab with nothing in it; a shell resolves PATH the way the
        // user's own terminal does.
        var shell = $"powershell.exe -NoExit -Command {command}";
        return TryStart("wt.exe", $"new-tab {shell}")
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
