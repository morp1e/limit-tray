using System.Text.Json;

namespace LimitTray.Core.Codex;

internal static class CodexProtocol
{
    public const string InitializeMessage = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"limit-tray","title":"Lim'it","version":"0.4.0"}}}""";
    public const string InitializedNotification = """{"jsonrpc":"2.0","method":"initialized","params":{}}""";
    public const string ReadMessage = """{"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}""";

    public static bool IsResponse(string line, int id)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("id", out var responseId) &&
                   responseId.ValueKind == JsonValueKind.Number &&
                   responseId.TryGetInt32(out var value) &&
                   value == id &&
                   !root.TryGetProperty("method", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
