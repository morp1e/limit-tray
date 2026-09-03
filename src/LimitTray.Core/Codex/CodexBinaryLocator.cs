namespace LimitTray.Core.Codex;

/// <summary>
/// On Windows, `codex` on PATH is an npm shim and cannot be started directly
/// as a process (WinError 2). The real binary is in the npm vendor directory.
/// </summary>
public static class CodexBinaryLocator
{
    public static string? Locate(
        IEnumerable<string> candidatePaths, Func<string, bool> fileExists) =>
        candidatePaths.FirstOrDefault(fileExists);

    public static string? LocateDefault() => Locate(DefaultCandidates(), File.Exists);

    public static IEnumerable<string> DefaultCandidates()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmRoot = Path.Combine(appData, "npm", "node_modules", "@openai", "codex");

        yield return Path.Combine(npmRoot, "node_modules", "@openai",
            "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Programs", "codex", "codex.exe");
    }
}
