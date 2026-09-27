using System.Runtime.CompilerServices;
using LimitTray.Core.Collectors;
using LimitTray.Core.Model;

namespace LimitTray.Core.Codex;

public sealed class CodexCollector : IQuotaCollector
{
    public static readonly TimeSpan RolloutPollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ServerReadInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AfterResetDelay = TimeSpan.FromSeconds(30);
    public const int FailuresBeforeBroken = 3;

    private readonly Func<CancellationToken, Task<QuotaSnapshot>> _readServer;
    private readonly Func<QuotaSnapshot?> _pollRollout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _activeWait;
    private int _refreshRequested;

    public CodexCollector(
        Func<CancellationToken, Task<QuotaSnapshot>> readServer,
        Func<QuotaSnapshot?> pollRollout,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _readServer = readServer;
        _pollRollout = pollRollout;
        _clock = clock;
        _delay = delay;
    }

    public string Provider => CodexRateLimitsParser.Provider;

    public void RequestRefresh()
    {
        Interlocked.Exchange(ref _refreshRequested, 1);
        try
        {
            Volatile.Read(ref _activeWait)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The loop already completed the wait; the flag is checked before its next wait.
        }
    }

    public async IAsyncEnumerable<QuotaSnapshot> Watch(
        [EnumeratorCancellation] CancellationToken ct)
    {
        QuotaSnapshot? latest = null;
        var failures = 0;
        var staledResets = new HashSet<DateTimeOffset>();
        var nextPoll = _clock();
        var nextServer = nextPoll;

        while (!ct.IsCancellationRequested)
        {
            var emissions = new List<QuotaSnapshot>();
            void Offer(QuotaSnapshot snapshot)
            {
                if (latest is not null && snapshot.FetchedAt <= latest.FetchedAt) return;

                latest = snapshot with
                {
                    Session = snapshot.Session ?? latest?.Session,
                    Weekly = snapshot.Weekly ?? latest?.Weekly,
                };
                emissions.Add(latest);
            }

            var now = _clock();

            if (Interlocked.Exchange(ref _refreshRequested, 0) != 0)
                nextServer = now;

            if (now >= nextPoll)
            {
                var rollout = _pollRollout();
                if (rollout is not null) Offer(rollout);
                nextPoll = now + RolloutPollInterval;
            }

            var due = NextResetRead(latest, staledResets);
            if (now >= nextServer || (due is not null && now >= due.Value))
            {
                var cancelled = false;
                try
                {
                    var snapshot = await _readServer(ct).ConfigureAwait(false);
                    if (snapshot.Health == HealthState.Fresh)
                    {
                        failures = 0;
                        Offer(snapshot);
                    }
                    else
                    {
                        failures++;
                        if (failures == FailuresBeforeBroken)
                            emissions.Add(snapshot);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    failures++;
                    if (failures == FailuresBeforeBroken)
                    {
                        emissions.Add(QuotaSnapshot.Unhealthy(
                            Provider, HealthState.ProtocolBroken, _clock(), ex.GetType().Name));
                    }
                }

                if (cancelled) yield break;

                now = _clock();
                if (latest is not null)
                {
                    foreach (var window in Windows(latest))
                    {
                        if (window.ResetsAt is not { } reset || latest.FetchedAt >= reset ||
                            reset > now - AfterResetDelay || !staledResets.Add(reset))
                            continue;

                        emissions.Add(latest with { Health = HealthState.Stale });
                    }
                }

                nextServer = now + ServerReadInterval;
            }

            foreach (var emission in emissions)
                yield return emission;

            if (ct.IsCancellationRequested) yield break;
            if (Interlocked.Exchange(ref _refreshRequested, 0) != 0)
            {
                nextServer = _clock();
                continue;
            }

            due = NextResetRead(latest, staledResets);
            var wake = due is { } resetDue
                ? Min(nextPoll, nextServer, resetDue)
                : Min(nextPoll, nextServer);
            var wait = wake - _clock();
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Volatile.Write(ref _activeWait, waitCts);
            var waitCancelled = false;
            try
            {
                if (Interlocked.Exchange(ref _refreshRequested, 0) != 0)
                {
                    nextServer = _clock();
                    continue;
                }

                await _delay(wait, waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                waitCancelled = true;
            }
            finally
            {
                Interlocked.CompareExchange(ref _activeWait, null, waitCts);
            }

            if (waitCancelled && ct.IsCancellationRequested) yield break;
        }
    }

    private static DateTimeOffset? NextResetRead(
        QuotaSnapshot? latest, HashSet<DateTimeOffset> staledResets)
    {
        if (latest is null) return null;

        DateTimeOffset? due = null;
        foreach (var window in Windows(latest))
        {
            if (window.ResetsAt is not { } reset || latest.FetchedAt >= reset ||
                staledResets.Contains(reset))
                continue;

            var candidate = reset + AfterResetDelay;
            if (due is null || candidate < due) due = candidate;
        }

        return due;
    }

    private static IEnumerable<QuotaWindow> Windows(QuotaSnapshot snapshot)
    {
        if (snapshot.Session is not null) yield return snapshot.Session;
        if (snapshot.Weekly is not null) yield return snapshot.Weekly;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b, DateTimeOffset c) =>
        Min(Min(a, b), c);
}
