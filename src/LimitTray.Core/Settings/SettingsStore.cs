using System.Text;

namespace LimitTray.Core.Settings;

/// <summary>
/// Same contract as HistoryStore: never throws, an unusable file is a first run, a
/// lost write is reported through <see cref="LastSaveFailed"/> rather than by taking
/// the application down. The settings page shows one line when the flag is set.
/// </summary>
public sealed class SettingsStore
{
    private readonly Func<string?> _read;
    private readonly Action<string> _write;
    private string? _lastWritten;

    public SettingsStore(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
    }

    public static SettingsStore ForDefaultPath() =>
        new(() => File.Exists(DefaultPath) ? File.ReadAllText(DefaultPath) : null,
            content => WriteFile(DefaultPath, content));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "limit-tray", "settings.json");

    public bool LastSaveFailed { get; private set; }

    public AppSettings Load()
    {
        string? content;
        try { content = _read(); }
        catch (Exception) { return AppSettings.Default; }

        if (string.IsNullOrWhiteSpace(content)) return AppSettings.Default;

        var settings = SettingsFile.Read(content);
        if (settings is null) return AppSettings.Default;

        _lastWritten = content;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var content = SettingsFile.Write(settings);
        if (string.Equals(content, _lastWritten, StringComparison.Ordinal))
        {
            // Back at the value that is on disk: nothing is lost any more.
            LastSaveFailed = false;
            return;
        }

        try
        {
            _write(content);
            _lastWritten = content;
            LastSaveFailed = false;
        }
        catch (Exception)
        {
            LastSaveFailed = true;
        }
    }

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
