using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using LimitTray.Core.Process;

namespace LimitTray.Tests.Codex;

public class CodexServerReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private const string InitOk = """{"jsonrpc":"2.0","id":1,"result":{}}""";
    private const string ReadOk = """{"jsonrpc":"2.0","id":2,"result":{"rateLimits":{"primary":{"usedPercent":18,"windowDurationMins":300,"resetsAt":1790479554},"secondary":{"usedPercent":22,"windowDurationMins":10080,"resetsAt":1791047422}}}}""";
    private const string Notice = """{"jsonrpc":"2.0","method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":99}}}}""";

    private sealed class ScriptedProcess : IJsonRpcProcess
    {
        private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
        private readonly Func<string, IEnumerable<string>> _reply;
        public List<string> Sent { get; } = new();
        public int Disposed { get; private set; }
        public bool ThrowOnStart { get; init; }
        public ScriptedProcess(Func<string, IEnumerable<string>> reply) => _reply = reply;
        public Task StartAsync(CancellationToken ct) =>
            ThrowOnStart ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        public Task SendAsync(string line, CancellationToken ct)
        {
            Sent.Add(line);
            foreach (var r in _reply(line)) _out.Writer.TryWrite(r);
            return Task.CompletedTask;
        }
        public void End() => _out.Writer.TryComplete();
        public async IAsyncEnumerable<string> ReadLines([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var l in _out.Reader.ReadAllAsync(ct)) yield return l;
        }
        public void Dispose() => Disposed++;
    }

    private static IEnumerable<string> Happy(string sent) =>
        sent.Contains("\"initialize\"") ? new[] { InitOk } :
        sent.Contains("rateLimits/read") ? new[] { Notice, ReadOk } :
        Array.Empty<string>();

    [Fact]
    public async Task ReadOnce_HappyPath_ReturnsFreshAndDisposesOnce()
    {
        var p = new ScriptedProcess(Happy);
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.Fresh, snap.Health);
        Assert.Equal(18, snap.Session!.Percent);
        Assert.Equal(22, snap.Weekly!.Percent);
        Assert.Equal(1, p.Disposed);
        Assert.Contains(p.Sent, s => s.Contains("\"initialized\""));
    }

    [Fact]
    public async Task ReadOnce_NotificationBeforeResponse_IsIgnored()
    {
        var p = new ScriptedProcess(Happy);
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);
        Assert.NotEqual(99, snap.Session!.Percent);
    }

    [Fact]
    public async Task ReadOnce_InitializeNeverAnswered_TimesOutAsBroken()
    {
        var p = new ScriptedProcess(_ => Array.Empty<string>());
        var snap = await new CodexServerReader(() => p, () => Now, TimeSpan.FromMilliseconds(100))
            .ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal("app-server zaman asimi", snap.Detail);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_ProcessEndsEarly_IsBroken()
    {
        ScriptedProcess? p = null;
        p = new ScriptedProcess(sent => { if (sent.Contains("\"initialize\"")) p!.End(); return Array.Empty<string>(); });
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal("app-server erken kapandi", snap.Detail);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_FactoryThrows_IsBrokenWithTypeNameOnly()
    {
        var snap = await new CodexServerReader(() => throw new FileNotFoundException("C:\\secret\\path"), () => Now)
            .ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal("app-server baslatilamadi: FileNotFoundException", snap.Detail);
    }

    [Fact]
    public async Task ReadOnce_StartThrows_IsBrokenAndDisposed()
    {
        var p = new ScriptedProcess(Happy) { ThrowOnStart = true };
        var snap = await new CodexServerReader(() => p, () => Now).ReadOnceAsync(CancellationToken.None);

        Assert.Equal(HealthState.ProtocolBroken, snap.Health);
        Assert.Equal("app-server baslatilamadi: InvalidOperationException", snap.Detail);
        Assert.Equal(1, p.Disposed);
    }

    [Fact]
    public async Task ReadOnce_Cancelled_Throws()
    {
        var p = new ScriptedProcess(_ => Array.Empty<string>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CodexServerReader(() => p, () => Now).ReadOnceAsync(cts.Token));
    }
}
