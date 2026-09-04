using System.Text;

namespace LimitTray.Core.History;

/// <summary>
/// Loads and saves the usage history. Every failure path is silent and behaves like a
/// first run: a quota panel that refuses to start because a cache file is unreadable
/// would be worse than one that simply has no history yet.
/// </summary>
public sealed class HistoryStore
{
    private readonly Func<string?> _read;
    private readonly Action<string> _write;
    private string? _lastWritten;

    public HistoryStore(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    public static HistoryStore ForDefaultPath() =>
        new(() => ReadFile(DefaultPath), content => WriteFile(DefaultPath, content));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "limit-tray", "history.json");

    /// <summary>Never throws and never returns null; an unusable file yields empty history.</summary>
    public UsageHistory Load()
    {
        string? content;
        try
        {
            content = _read();
        }
        catch (Exception)
        {
            return new UsageHistory();
        }

        if (string.IsNullOrWhiteSpace(content)) return new UsageHistory();

        var history = HistoryFile.Read(content);
        if (history is null) return new UsageHistory();

        _lastWritten = content;
        return history;
    }

    /// <summary>
    /// Writes the history when it has actually changed. Never throws: losing a cache
    /// write is not worth taking the application down for.
    /// </summary>
    public void Save(UsageHistory history)
    {
        string content;
        try
        {
            content = HistoryFile.Write(history);
        }
        catch (Exception)
        {
            return;
        }

        if (string.Equals(content, _lastWritten, StringComparison.Ordinal)) return;

        try
        {
            _write(content);
            _lastWritten = content;
        }
        catch (Exception)
        {
            // Left unwritten on purpose; the next save will try again.
        }
    }

    private static string? ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Written through a temporary file so an interrupted write cannot leave a
        // half-document behind. A truncated file would still be handled, but silently
        // losing the history on every crash is avoidable.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
