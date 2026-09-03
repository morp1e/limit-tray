using AgentQuotaTray.Core.Model;

namespace AgentQuotaTray.Core.Store;

/// <summary>
/// Son snapshot'lari tutar ve yasa gore Fresh -> Stale gecisini yonetir.
/// Hata durumlari (RateLimited, AuthMissing, ProtocolBroken) yas gectikce
/// Stale'e cevrilmez; hata bilgisi daha spesifiktir ve korunur.
/// </summary>
public sealed class QuotaStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public QuotaStore(Func<DateTimeOffset> clock) => _clock = clock;

    public event Action<QuotaSnapshot>? Changed;

    public void Apply(QuotaSnapshot snapshot)
    {
        lock (_gate) _snapshots[snapshot.Provider] = snapshot;
        Changed?.Invoke(snapshot);
    }

    public QuotaSnapshot? Get(string provider)
    {
        lock (_gate) return _snapshots.GetValueOrDefault(provider);
    }

    public IReadOnlyList<QuotaSnapshot> All()
    {
        lock (_gate)
            return _snapshots.Values.OrderBy(s => s.Provider, StringComparer.Ordinal).ToList();
    }

    public void RefreshStaleness()
    {
        var now = _clock();
        var transitioned = new List<QuotaSnapshot>();

        lock (_gate)
        {
            foreach (var (provider, snapshot) in _snapshots.ToList())
            {
                if (snapshot.Health != HealthState.Fresh) continue;
                if (now - snapshot.FetchedAt < StaleAfter) continue;

                var stale = snapshot with { Health = HealthState.Stale };
                _snapshots[provider] = stale;
                transitioned.Add(stale);
            }
        }

        foreach (var snapshot in transitioned) Changed?.Invoke(snapshot);
    }
}
