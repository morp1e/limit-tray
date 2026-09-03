using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("LimitTray.Tests")]

namespace LimitTray.Core.Process;

public sealed class StdioJsonRpcProcess : IJsonRpcProcess
{
    /// <summary>
    /// Must be WITHOUT a BOM. The Encoding.UTF8 constant produces a BOM and
    /// sends EF BB BF at the start of the first line; the app-server cannot parse
    /// it, writes "Failed to deserialize JSONRPCMessage: expected value at line 1 column 1"
    /// to stderr, and sends NO response. Measured 2026-09-03: BOM-free sends
    /// receive an initialize response, while BOM-bearing sends receive zero lines.
    /// </summary>
    internal static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _fileName;
    private readonly string _arguments;
    private System.Diagnostics.Process? _process;

    public StdioJsonRpcProcess(string fileName, string arguments)
    {
        _fileName = fileName;
        _arguments = arguments;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var info = new ProcessStartInfo(_fileName, _arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardInputEncoding = Utf8NoBom,
        };

        _process = System.Diagnostics.Process.Start(info)
            ?? throw new InvalidOperationException("codex app-server baslatilamadi");

        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonLine, CancellationToken ct)
    {
        var process = _process
            ?? throw new InvalidOperationException("Surec baslatilmadi");
        await process.StandardInput.WriteLineAsync(jsonLine.AsMemory(), ct)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ReadLines(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var process = _process
            ?? throw new InvalidOperationException("Surec baslatilmadi");

        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            yield return line;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        _process?.Dispose();
    }
}
