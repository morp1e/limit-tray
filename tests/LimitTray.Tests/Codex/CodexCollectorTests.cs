using LimitTray.Core.Codex;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Codex;

public class CodexCollectorTests
{
    private sealed class Harness
    {
        public DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        public readonly List<TimeSpan> Delays = new();
        public readonly Queue<QuotaSnapshot?> Rollout = new();
        public readonly Queue<QuotaSnapshot> Server = new();
        public int ServerReads, RolloutPolls;
        public CodexCollector Build() => new(
            _ => { ServerReads++; return Task.FromResult(Server.Count > 0 ? Server.Dequeue() : Broken()); },
            () => { RolloutPolls++; return Rollout.Count > 0 ? Rollout.Dequeue() : null; },
            () => Now,
            (d, ct) => { ct.ThrowIfCancellationRequested(); Delays.Add(d); Now += d; return Task.CompletedTask; });
        public QuotaSnapshot Fresh(double s, double w, DateTimeOffset at, DateTimeOffset? sessionReset = null) =>
            new("codex", new QuotaWindow(s, sessionReset ?? at.AddHours(5), TimeSpan.FromHours(5)),
                new QuotaWindow(w, at.AddDays(7), TimeSpan.FromDays(7)), HealthState.Fresh, at, null);
        public QuotaSnapshot Broken() =>
            QuotaSnapshot.Unhealthy("codex", HealthState.ProtocolBroken, Now, "app-server zaman asimi");
    }

    private static async Task<List<QuotaSnapshot>> Run(CodexCollector c, int take)
    {
        var got = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var s in c.Watch(cts.Token))
        {
            got.Add(s);
            if (got.Count == take) break;
        }
        return got;
    }

    [Fact]
    public async Task Start_PollsRolloutAndReadsServer_BeforeFirstWait()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        var got = await Run(h.Build(), 1);

        Assert.Equal(1, h.RolloutPolls);
        Assert.Equal(1, h.ServerReads);
        Assert.Empty(h.Delays);
        Assert.Equal(10, got[0].Session!.Percent);
    }

    [Fact]
    public async Task Server_IsReadEveryTenMinutes_RolloutEveryThirtySeconds()
    {
        var h = new Harness();
        for (var i = 0; i < 3; i++) h.Server.Enqueue(h.Fresh(10 + i, 20, h.Now.AddMinutes(10 * i)));
        await Run(h.Build(), 3);

        Assert.Equal(3, h.ServerReads);
        Assert.Equal(41, h.RolloutPolls);                       // t=0, then every 30 s for 20 min
        Assert.All(h.Delays, d => Assert.True(d <= TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task OlderReading_IsNotEmitted_NewerIs()
    {
        var h = new Harness();
        h.Rollout.Enqueue(h.Fresh(30, 30, h.Now));              // newest
        h.Server.Enqueue(h.Fresh(10, 10, h.Now.AddMinutes(-5)));// older, must be dropped
        h.Rollout.Enqueue(h.Fresh(31, 31, h.Now.AddSeconds(30)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(new[] { 30.0, 31.0 }, got.Select(s => s.Session!.Percent));
    }

    [Fact]
    public async Task PartialReading_KeepsTheOtherWindow()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        h.Rollout.Enqueue(null);
        h.Rollout.Enqueue(new QuotaSnapshot("codex", new QuotaWindow(11, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, h.Now.AddSeconds(30), null));
        var got = await Run(h.Build(), 2);

        Assert.Equal(11, got[1].Session!.Percent);
        Assert.Equal(20, got[1].Weekly!.Percent);
    }

    [Fact]
    public async Task ResetPassed_AndReadFails_EmitsStaleWithTheOldPercent_NeverZero()
    {
        var h = new Harness();
        var start = h.Now;
        h.Server.Enqueue(h.Fresh(70, 20, start, sessionReset: start.AddMinutes(3)));
        // every later server read fails
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.Stale, got[1].Health);
        Assert.Equal(70, got[1].Session!.Percent);
        Assert.True(h.Now >= start.AddMinutes(3).AddSeconds(30));
        Assert.True(h.Now < start.AddMinutes(10));              // not waiting for the 10-minute read
    }

    [Fact]
    public async Task ResetPassed_AndReadSucceeds_EmitsFreshOnly()
    {
        var h = new Harness();
        var start = h.Now;
        h.Server.Enqueue(h.Fresh(70, 20, start, sessionReset: start.AddMinutes(3)));
        h.Server.Enqueue(h.Fresh(2, 21, start.AddMinutes(3).AddSeconds(31)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.Fresh, got[1].Health);
        Assert.Equal(2, got[1].Session!.Percent);
    }

    [Fact]
    public async Task ThreeFailedReads_EmitProtocolBrokenOnce_ThenRolloutRecovers()
    {
        var h = new Harness();                                 // server queue empty: every read fails
        for (var i = 0; i < 50; i++) h.Rollout.Enqueue(null);  // polls at 0 s .. 24 min 30 s are quiet
        h.Rollout.Enqueue(h.Fresh(12, 13, h.Now.AddMinutes(25)));
        var got = await Run(h.Build(), 2);

        Assert.Equal(HealthState.ProtocolBroken, got[0].Health);
        Assert.Equal(HealthState.Fresh, got[1].Health);
        Assert.Equal(3, h.ServerReads);                        // t=0, 10, 20 min; no fast retry
    }

    [Fact]
    public async Task RequestRefresh_CutsTheWaitShortAndReads()
    {
        var h = new Harness();
        h.Server.Enqueue(h.Fresh(10, 20, h.Now));
        h.Server.Enqueue(h.Fresh(15, 20, h.Now.AddSeconds(1)));
        var collector = new CodexCollector(
            _ => { h.ServerReads++; return Task.FromResult(h.Server.Count > 0 ? h.Server.Dequeue() : h.Broken()); },
            () => null,
            () => h.Now,
            async (d, ct) => { await Task.Delay(Timeout.Infinite, ct); });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var got = new List<QuotaSnapshot>();
        await foreach (var s in collector.Watch(cts.Token))
        {
            got.Add(s);
            if (got.Count == 1) { h.Now = h.Now.AddSeconds(1); collector.RequestRefresh(); }
            if (got.Count == 2) break;
        }

        Assert.Equal(15, got[1].Session!.Percent);
        Assert.Equal(2, h.ServerReads);
    }

    [Fact]
    public async Task Cancellation_EndsTheStreamWithoutThrowing()
    {
        var h = new Harness();
        var collector = new CodexCollector(_ => Task.FromResult(h.Broken()), () => null, () => h.Now,
            async (d, ct) => await Task.Delay(Timeout.Infinite, ct));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var ex = await Record.ExceptionAsync(async () => { await foreach (var _ in collector.Watch(cts.Token)) { } });
        Assert.Null(ex);
    }
}

