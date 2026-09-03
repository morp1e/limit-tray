using AgentQuotaTray.Core.Claude;
using AgentQuotaTray.Core.Http;
using AgentQuotaTray.Core.Model;
using Xunit;

namespace AgentQuotaTray.Tests.Claude;

public class ClaudeCollectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeTransport : IHttpTransport
    {
        private readonly Queue<HttpTransportResult> _results;
        public List<IReadOnlyDictionary<string, string>> SeenHeaders { get; } = new();
        public FakeTransport(params HttpTransportResult[] results) =>
            _results = new Queue<HttpTransportResult>(results);

        public Task<HttpTransportResult> GetAsync(
            string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            SeenHeaders.Add(headers);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private static ClaudeCollector Build(
        IHttpTransport transport, string? token, List<TimeSpan>? delays = null) =>
        new(transport,
            new ClaudeCredentialReader(() => token),
            () => Now,
            (d, _) => { delays?.Add(d); return Task.CompletedTask; });

    private static async Task<List<QuotaSnapshot>> Take(
        ClaudeCollector collector, int count)
    {
        var result = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource();
        await foreach (var snap in collector.Watch(cts.Token))
        {
            result.Add(snap);
            if (result.Count >= count) { cts.Cancel(); break; }
        }
        return result;
    }

    [Fact]
    public async Task Watch_Success_YieldsFreshSnapshot()
    {
        var body = """{"five_hour":{"utilization":14.0},"seven_day":{"utilization":12.0}}""";
        var transport = new FakeTransport(new HttpTransportResult(200, body));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(14.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_SendsBearerAndBetaHeaders()
    {
        var transport = new FakeTransport(
            new HttpTransportResult(200, """{"five_hour":{"utilization":1.0}}"""));

        await Take(Build(transport, "tok-abc"), 1);

        var headers = transport.SeenHeaders[0];
        Assert.Equal("Bearer tok-abc", headers["Authorization"]);
        Assert.Equal("oauth-2025-04-20", headers["anthropic-beta"]);
    }

    [Fact]
    public async Task Watch_NoToken_YieldsAuthMissing()
    {
        var snaps = await Take(Build(new FakeTransport(), token: null), 1);

        Assert.Equal(HealthState.AuthMissing, snaps[0].Health);
        Assert.Null(snaps[0].Session);
    }

    [Fact]
    public async Task Watch_401_YieldsAuthMissing()
    {
        var transport = new FakeTransport(new HttpTransportResult(401, "{}"));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.AuthMissing, snaps[0].Health);
    }

    [Fact]
    public async Task Watch_429_YieldsRateLimitedAndBacksOff()
    {
        // Snapshot ONCE yayilir, bekleme SONRA gelir. Bu yuzden N gecikme gozlemek
        // icin N+1 snapshot alinir; aksi halde son bekleme hic calismaz.
        var delays = new List<TimeSpan>();
        var transport = new FakeTransport(
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(429, "{}"));

        var snaps = await Take(Build(transport, "tok-abc", delays), 3);

        Assert.All(snaps, s => Assert.Equal(HealthState.RateLimited, s.Health));
        Assert.Equal(TimeSpan.FromMinutes(2), delays[0]);
        Assert.Equal(TimeSpan.FromMinutes(4), delays[1]);
    }

    [Fact]
    public async Task Watch_BackoffIsCappedAt15Minutes()
    {
        var delays = new List<TimeSpan>();
        var results = Enumerable.Range(0, 8)
            .Select(_ => new HttpTransportResult(429, "{}")).ToArray();

        await Take(Build(new FakeTransport(results), "tok-abc", delays), 8);

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromMinutes(15)));
        Assert.Equal(TimeSpan.FromMinutes(15), delays[^1]);
    }

    [Fact]
    public async Task Watch_500_YieldsProtocolBroken()
    {
        var transport = new FakeTransport(new HttpTransportResult(500, "oops"));

        var snaps = await Take(Build(transport, "tok-abc"), 1);

        Assert.Equal(HealthState.ProtocolBroken, snaps[0].Health);
    }

    [Fact]
    public async Task Watch_SuccessAfter429_ResetsBackoffToNormalInterval()
    {
        var delays = new List<TimeSpan>();
        var transport = new FakeTransport(
            new HttpTransportResult(429, "{}"),
            new HttpTransportResult(200, """{"five_hour":{"utilization":5.0}}"""),
            new HttpTransportResult(200, """{"five_hour":{"utilization":5.0}}"""));

        await Take(Build(transport, "tok-abc", delays), 3);

        Assert.Equal(TimeSpan.FromMinutes(2), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(60), delays[1]);
    }

    [Fact]
    public async Task Watch_ErrorDetail_NeverContainsToken()
    {
        var transport = new FakeTransport(new HttpTransportResult(500, "oops"));

        var snaps = await Take(Build(transport, "super-secret-token"), 1);

        Assert.DoesNotContain("super-secret-token", snaps[0].Detail ?? "");
    }
}
