using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentQuotaTray.Core.Process;

public sealed class StdioJsonRpcProcess : IJsonRpcProcess
{
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
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
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
