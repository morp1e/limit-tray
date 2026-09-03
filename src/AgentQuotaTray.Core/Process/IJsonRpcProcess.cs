namespace AgentQuotaTray.Core.Process;

public interface IJsonRpcProcess : IDisposable
{
    Task StartAsync(CancellationToken ct);
    Task SendAsync(string jsonLine, CancellationToken ct);
    IAsyncEnumerable<string> ReadLines(CancellationToken ct);
}
