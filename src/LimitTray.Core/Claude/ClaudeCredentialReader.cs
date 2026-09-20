using System.Text.Json;

namespace LimitTray.Core.Claude;

/// <summary>
/// Reads the token fresh from disk on every call; Claude Code may have refreshed it.
/// The token is never stored in a field and is never logged.
/// </summary>
public sealed class ClaudeCredentialReader
{
    private readonly Func<string?> _read;

    public ClaudeCredentialReader(Func<string?> read) => _read = read;

    public static ClaudeCredentialReader FromDefaultPath() =>
        new(() => ReadFromFile(DefaultPath));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    public string? ReadToken() => _read();

    private static string? ReadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The token inside the credentials document, or null for any shape that is not
    /// an object holding claudeAiOauth.accessToken as a string. A changed file shape
    /// means "no token", never an exception: the collector must keep running.
    /// </summary>
    public static string? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object) return null;
            if (!oauth.TryGetProperty("accessToken", out var token)
                || token.ValueKind != JsonValueKind.String) return null;
            var value = token.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
