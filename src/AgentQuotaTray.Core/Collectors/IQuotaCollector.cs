using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Collectors;

public interface IQuotaCollector
{
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);
}
