using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using LimitTray.Core.Process;
using Xunit;

namespace LimitTray.Tests.Codex;

public class CodexCollectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeProcess : IJsonRpcProcess
    {
        private readonly string[] _lines;
        private readonly bool _failStart;
        public List<string> Sent { get; } = new();

        public FakeProcess(string[] lines, bool failStart = false)
        {
            _lines = lines;
            _failStart = failStart;
        }

        public Task StartAsync(CancellationToken ct) =>
            _failStart
                ? Task.FromException(new InvalidOperationException("baslatilamadi"))
                : Task.CompletedTask;

        public Task SendAsync(string jsonLine, CancellationToken ct)
        {
            Sent.Add(jsonLine);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ReadLines(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in _lines)
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }

        public void Dispose() { }
    }

    private sealed class OpenStreamProcess : IJsonRpcProcess
    {
        private readonly TaskCompletionSource<bool> _secondRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public List<string> Sent { get; } = new();

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        public Task SendAsync(string jsonLine, CancellationToken ct)
        {
            Sent.Add(jsonLine);
            if (jsonLine.Contains("account/rateLimits/read", StringComparison.Ordinal) &&
                Interlocked.Increment(ref _readCount) >= 2)
                _secondRead.TrySetResult(true);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ReadLines(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return InitLine;
            await _secondRead.Task.WaitAsync(ct);
            yield return ReadLine;
        }

        public void Dispose() { }
    }

    private const string InitLine = """{"id":1,"result":{"userAgent":"x"}}""";
    private const string ReadLine = """
    {"id":2,"result":{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788478826},"secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1788817184}}}}
    """;
    private const string UpdatedLine = """
    {"method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":8,"windowDurationMins":300,"resetsAt":1788478826}}}}
    """;

    private static async Task<List<QuotaSnapshot>> Take(CodexCollector collector, int count)
    {
        var result = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource();
        try
        {
            await foreach (var snap in collector.Watch(cts.Token))
            {
                result.Add(snap);
                if (result.Count >= count) { await cts.CancelAsync(); break; }
            }
        }
        catch (OperationCanceledException) { }
        return result;
    }

    [Fact]
    public async Task Watch_ReadResponse_YieldsFreshSnapshot()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine });
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(0.0, snaps[0].Session!.Percent);
        Assert.Equal(36.0, snaps[0].Weekly!.Percent);
    }

    [Fact]
    public async Task Watch_SendsInitializeThenRead()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine });
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        await Take(collector, 1);

        Assert.Contains(process.Sent, s => s.Contains("\"initialize\""));
        Assert.Contains(process.Sent, s => s.Contains("account/rateLimits/read"));
        var initIndex = process.Sent.FindIndex(s => s.Contains("\"initialize\""));
        var readIndex = process.Sent.FindIndex(s => s.Contains("rateLimits/read"));
        Assert.True(initIndex < readIndex);
    }

    [Fact]
    public async Task Watch_PeriodicallyResendsRead_SoPushOnlyDataDoesNotGoStale()
    {
        var process = new OpenStreamProcess();
        var delayCalls = 0;
        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = new CodexCollector(
            () => process,
            () => Now,
            (_, ct) => Interlocked.Increment(ref delayCalls) == 1
                ? Task.CompletedTask
                : never.Task.WaitAsync(ct),
            () => null);

        await Take(collector, 1);

        Assert.True(process.Sent.Count(s => s.Contains("account/rateLimits/read")) > 1);
    }

    [Fact]
    public async Task Watch_UpdatedNotification_YieldsNewSnapshot()
    {
        var process = new FakeProcess(new[] { InitLine, ReadLine, UpdatedLine });
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 2);

        Assert.Equal(0.0, snaps[0].Session!.Percent);
        Assert.Equal(8.0, snaps[1].Session!.Percent);
    }

    [Fact]
    public async Task Watch_UnrelatedLines_AreIgnored()
    {
        var noise = """{"method":"remoteControl/status/changed","params":{"status":"disabled"}}""";
        var process = new FakeProcess(new[] { InitLine, noise, ReadLine });
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.Fresh, snaps[0].Health);
        Assert.Equal(0.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_StartFailsThreeTimes_FallsBackToRolloutFile()
    {
        var fallback = new QuotaSnapshot(
            "codex", new QuotaWindow(11.0, null, TimeSpan.FromHours(5)), null,
            HealthState.Stale, Now, "dosyadan");
        var attempts = 0;

        // The attempt count is measured when the fallback is REQUESTED. Reading
        // it at the end would race: RunLoop continues in the background and
        // cancellation does not take effect immediately, so the final count may
        // exceed three. What matters is that the fallback was requested on the
        // exact third failure.
        var attemptsWhenFallbackRequested = 0;

        var collector = new CodexCollector(
            () => { attempts++; return new FakeProcess(Array.Empty<string>(), failStart: true); },
            () => Now,
            (_, _) => Task.CompletedTask,
            () =>
            {
                // The fallback may be requested multiple times (once every three
                // failures). Measure the FIRST request; if later requests overwrite
                // it, the test measures how many loop turns have elapsed, not behavior.
                if (attemptsWhenFallbackRequested == 0)
                    attemptsWhenFallbackRequested = attempts;
                return fallback;
            });

        var snaps = await Take(collector, 1);

        Assert.Equal(3, attemptsWhenFallbackRequested);
        Assert.Equal(HealthState.Stale, snaps[0].Health);
        Assert.Equal(11.0, snaps[0].Session!.Percent);
    }

    [Fact]
    public async Task Watch_StartFailsAndNoFallback_YieldsProtocolBroken()
    {
        var collector = new CodexCollector(
            () => new FakeProcess(Array.Empty<string>(), failStart: true),
            () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.ProtocolBroken, snaps[0].Health);
        Assert.Null(snaps[0].Session);
    }

    [Fact]
    public async Task Watch_RestartBackoffIsCappedAtFiveMinutes()
    {
        var delays = new List<TimeSpan>();
        var collector = new CodexCollector(
            () => new FakeProcess(Array.Empty<string>(), failStart: true),
            () => Now, (d, _) => { delays.Add(d); return Task.CompletedTask; }, () => null);

        await Take(collector, 1);

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromMinutes(5)));
    }
}
