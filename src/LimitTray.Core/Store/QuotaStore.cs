using LimitTray.Core.Model;

namespace LimitTray.Core.Store;

/// <summary>
/// Holds the latest snapshots and manages the Fresh -> Stale transition by age.
/// Error states (RateLimited, AuthMissing, ProtocolBroken) are not converted to
/// Stale as they age; the error information is more specific and is preserved.
/// </summary>
public sealed class QuotaStore
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, QuotaSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, TimeSpan> _staleAfter;
    private readonly object _gate = new();

    public QuotaStore(Func<DateTimeOffset> clock, Func<string, TimeSpan>? staleAfter = null)
    {
        _clock = clock;
        _staleAfter = staleAfter ?? (_ => StaleAfter);
    }

    public static TimeSpan StaleAfterFor(string provider, int refreshSeconds)
    {
        if (string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromMinutes(25);

        return TimeSpan.FromSeconds(Math.Max(StaleAfter.TotalSeconds, refreshSeconds * 2.5));
    }

    public event Action<QuotaSnapshot>? Changed;

    public void Apply(QuotaSnapshot snapshot)
    {
        QuotaSnapshot applied;
        lock (_gate)
        {
            if (snapshot.Session is null && snapshot.Weekly is null &&
                _snapshots.TryGetValue(snapshot.Provider, out var previous) &&
                (previous.Session is not null || previous.Weekly is not null))
            {
                applied = snapshot with
                {
                    Session = previous.Session,
                    Weekly = previous.Weekly,
                    FetchedAt = previous.FetchedAt,
                };
            }
            else
            {
                applied = snapshot;
            }

            _snapshots[snapshot.Provider] = applied;
        }

        Changed?.Invoke(applied);
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
                if (now - snapshot.FetchedAt < _staleAfter(provider)) continue;

                var stale = snapshot with { Health = HealthState.Stale };
                _snapshots[provider] = stale;
                transitioned.Add(stale);
            }
        }

        foreach (var snapshot in transitioned) Changed?.Invoke(snapshot);
    }
}
