using System.Runtime.CompilerServices;
using LimitTray.Core.Collectors;
using LimitTray.Core.Model;

namespace LimitTray.Core.Fixtures;

public sealed class FixtureCollector : IQuotaCollector
{
    private readonly IReadOnlyList<QuotaSnapshot> _sequence;
    private readonly SemaphoreSlim _refresh = new(0, 1);

    public FixtureCollector(string provider, IReadOnlyList<QuotaSnapshot> sequence)
    {
        Provider = provider;
        _sequence = sequence;
    }

    public string Provider { get; }

    public async IAsyncEnumerable<QuotaSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var snapshot in _sequence)
        {
            ct.ThrowIfCancellationRequested();
            yield return snapshot;
        }

        if (_sequence.Count == 0)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            yield break;
        }

        while (true)
        {
            await _refresh.WaitAsync(ct).ConfigureAwait(false);
            yield return _sequence[^1];
        }
    }

    public void RequestRefresh()
    {
        try { _refresh.Release(); }
        catch (SemaphoreFullException) { }
    }
}
