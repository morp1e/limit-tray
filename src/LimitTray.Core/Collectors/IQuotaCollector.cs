using LimitTray.Core.Model;

namespace LimitTray.Core.Collectors;

public interface IQuotaCollector
{
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);
}
