using System.Runtime.CompilerServices;
using LimitTray.Core.Collectors;
using LimitTray.Core.Http;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;

namespace LimitTray.Core.Claude;

public sealed class ClaudeCollector : IQuotaCollector
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "oauth-2025-04-20";

    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly IHttpTransport _transport;
    private readonly ClaudeCredentialReader _credentials;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ClaudeCollector(
        IHttpTransport transport,
        ClaudeCredentialReader credentials,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _transport = transport;
        _credentials = credentials;
        _clock = clock;
        _delay = delay;
    }

    public string Provider => ClaudeUsageParser.Provider;

    public async IAsyncEnumerable<QuotaSnapshot> Watch(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var backoff = FirstBackoff;

        while (!ct.IsCancellationRequested)
        {
            var (snapshot, rateLimited) = await FetchOnce(ct).ConfigureAwait(false);

            // ORDER IS CRITICAL: publish first, then wait. Reversing this leaves
            // the application empty for one full interval at startup and makes
            // every value appear one cycle late. Do not change this order to pass a test.
            yield return snapshot;

            if (ct.IsCancellationRequested) yield break;

            TimeSpan wait;
            if (rateLimited)
            {
                wait = backoff;
                backoff = backoff >= MaxBackoff
                    ? MaxBackoff
                    : Min(backoff + backoff, MaxBackoff);
            }
            else
            {
                wait = NormalInterval;
                backoff = FirstBackoff;
            }

            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private async Task<(QuotaSnapshot Snapshot, bool RateLimited)> FetchOnce(
        CancellationToken ct)
    {
        var now = _clock();
        var token = _credentials.ReadToken();

        if (token is null)
        {
            return (QuotaSnapshot.Unhealthy(
                Provider, HealthState.AuthMissing, now,
                "Claude oturumu bulunamadı, giriş gerekli"), false);
        }

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token,
            ["anthropic-beta"] = BetaHeader,
            ["Content-Type"] = "application/json",
        };

        HttpTransportResult result;
        try
        {
            result = await _transport.GetAsync(UsageUrl, headers, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now,
                "Baglanti kurulamadi: " + ex.GetType().Name), false);
        }

        return result.StatusCode switch
        {
            200 => (ClaudeUsageParser.Parse(result.Body, now), false),
            401 or 403 => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.AuthMissing, now,
                "Token geçersiz, giriş gerekli"), false),
            429 => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.RateLimited, now,
                Strings.Turkish.RateLimited), true),
            _ => (QuotaSnapshot.Unhealthy(
                Provider, HealthState.ProtocolBroken, now,
                $"Beklenmeyen yanit: HTTP {result.StatusCode}"), false),
        };
    }
}
