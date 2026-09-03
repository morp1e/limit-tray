using System.Text.Json;

namespace AgentQuotaTray.Core.Claude;

/// <summary>
/// Token'i her cagrida diskten taze okur; Claude Code onu yenilemis olabilir.
/// Token asla alanda saklanmaz ve asla loglanmaz.
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
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (!oauth.TryGetProperty("accessToken", out var token)) return null;
            var value = token.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
