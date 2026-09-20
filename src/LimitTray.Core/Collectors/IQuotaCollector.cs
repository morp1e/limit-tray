using LimitTray.Core.Model;

namespace LimitTray.Core.Collectors;

public interface IQuotaCollector
{
    string Provider { get; }
    IAsyncEnumerable<QuotaSnapshot> Watch(CancellationToken ct);

    /// <summary>
    /// Asks for a fetch now instead of at the next tick. Best effort: ignored while a
    /// provider is backing off after a 429, and a no-op when nothing is running.
    /// </summary>
    void RequestRefresh();
}
