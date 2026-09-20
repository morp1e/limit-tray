using LimitTray.Core.Claude;
using LimitTray.Core.Codex;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Process;
using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Review;

/// <summary>
/// Regressions for the defects found in the 2026-09-20 code review. Each test names
/// the behaviour that was wrong, so a future change that reintroduces it fails here.
/// </summary>
public class ReviewFixesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    // Parsers: valid JSON with the wrong shape is ProtocolBroken, never an exception.

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void ClaudeParser_NonObjectRoot_IsProtocolBroken(string json)
    {
        var snapshot = ClaudeUsageParser.Parse(json, Now);
        Assert.Equal(HealthState.ProtocolBroken, snapshot.Health);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public void CodexParser_NonObjectRoot_IsProtocolBroken(string json)
    {
        var snapshot = CodexRateLimitsParser.ParseAppServer(json, Now);
        Assert.Equal(HealthState.ProtocolBroken, snapshot.Health);
    }

    [Fact]
    public void Parsers_MalformedJson_DetailCarriesOnlyTheExceptionType()
    {
        const string body = "{\"five_hour\": SECRET-LOOKING-FRAGMENT";

        var claude = ClaudeUsageParser.Parse(body, Now);
        var codex = CodexRateLimitsParser.ParseAppServer(body, Now);

        Assert.DoesNotContain("SECRET", claude.Detail);
        Assert.DoesNotContain("SECRET", codex.Detail);
        Assert.Contains("Exception", claude.Detail); // JsonReaderException: the type name, nothing else
    }

    [Theory]
    [InlineData(140)]
    [InlineData(-5)]
    public void ClaudeParser_PercentOutsideRange_IsProtocolBroken(double percent)
    {
        var json = "{\"five_hour\":{\"utilization\":" + percent.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ",\"resets_at\":\"2026-09-20T13:00:00Z\"},\"seven_day\":{\"utilization\":10}}";
        var snapshot = ClaudeUsageParser.Parse(json, Now);
        Assert.Equal(HealthState.ProtocolBroken, snapshot.Health);
        Assert.Null(snapshot.Session);
    }

    [Fact]
    public void CredentialReader_NonStringToken_IsNoToken()
    {
        var reader = new ClaudeCredentialReader(() => null);
        Assert.Null(reader.ReadToken());

        // The file path goes through the same guard; exercised via the JSON shape helper.
        Assert.Null(ClaudeCredentialReader.FromJson("""{"claudeAiOauth":{"accessToken":123}}"""));
        Assert.Null(ClaudeCredentialReader.FromJson("""{"claudeAiOauth":"nope"}"""));
        Assert.Null(ClaudeCredentialReader.FromJson("[]"));
        Assert.Equal("tok", ClaudeCredentialReader.FromJson("""{"claudeAiOauth":{"accessToken":"tok"}}"""));
    }

    // Settings

    [Fact]
    public void SettingsFile_VersionTooLargeForInt_IsUnusableNotACrash() =>
        Assert.Null(SettingsFile.Read("""{"version":99999999999}"""));

    [Fact]
    public void SettingsStore_RevertingToTheSavedValue_ClearsTheFailureFlag()
    {
        var fail = false;
        var store = new SettingsStore(() => null, _ => { if (fail) throw new IOException(); });

        store.Save(AppSettings.Default);                                 // on disk now
        fail = true;
        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark }); // lost
        Assert.True(store.LastSaveFailed);
        store.Save(AppSettings.Default);                                 // back to what disk has

        Assert.False(store.LastSaveFailed);
    }

    // Thresholds and tooltip

    [Fact]
    public void WarningThreshold_IsInclusive()
    {
        var t = new QuotaThresholds(60, 85);
        Assert.Equal(QuotaSeverity.Warning, QuotaFormatter.SeverityFor(85, t));
        Assert.Equal(QuotaSeverity.Caution, QuotaFormatter.SeverityFor(84.99, t));
    }

    [Fact]
    public void Tooltip_RetainedWindowsUnderAFailure_SayTheFailure()
    {
        var snapshot = new QuotaSnapshot("claude",
            new QuotaWindow(50, Now.AddHours(1), TimeSpan.FromHours(5)),
            new QuotaWindow(25, Now.AddDays(1), TimeSpan.FromDays(7)),
            HealthState.RateLimited, Now, "429");

        var text = QuotaFormatter.Tooltip(new[] { snapshot }, Now, Strings.English);

        Assert.Contains("50%", text);
        Assert.Contains(Strings.English.RateLimited, text);
    }

    // Codex collector

    private sealed class ScriptedProcess : IJsonRpcProcess
    {
        private readonly string[] _lines;
        public List<string> Sent { get; } = new();
        public ScriptedProcess(params string[] lines) => _lines = lines;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(string jsonLine, CancellationToken ct) { Sent.Add(jsonLine); return Task.CompletedTask; }
        public async IAsyncEnumerable<string> ReadLines(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in _lines) { ct.ThrowIfCancellationRequested(); yield return line; await Task.Yield(); }
        }
        public void Dispose() { }
    }

    private const string Init = """{"id":1,"result":{}}""";
    private const string FullRead = """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":300},"secondary":{"usedPercent":40,"windowDurationMins":10080}}}}""";
    private const string PrimaryOnlyUpdate = """{"method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":300}}}}""";

    private static async Task<List<QuotaSnapshot>> Take(CodexCollector collector, int count)
    {
        var result = new List<QuotaSnapshot>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
    public async Task Codex_FactoryThrowing_ReachesTheFallbackInsteadOfStoppingForever()
    {
        var fallback = QuotaSnapshot.Unhealthy("codex", HealthState.Stale, Now, "file");
        var collector = new CodexCollector(
            () => throw new InvalidOperationException("codex.exe bulunamadi"),
            () => Now, (_, _) => Task.CompletedTask, () => fallback);

        var snaps = await Take(collector, 1);

        Assert.Single(snaps);
        Assert.Equal(HealthState.Stale, snaps[0].Health);
    }

    [Fact]
    public async Task Codex_PartialUpdate_KeepsTheOtherWindow()
    {
        var process = new ScriptedProcess(Init, FullRead, PrimaryOnlyUpdate);
        var collector = new CodexCollector(() => process, () => Now, (_, _) => Task.CompletedTask, () => null);

        var snaps = await Take(collector, 2);

        Assert.Equal(12, snaps[1].Session!.Percent);
        Assert.NotNull(snaps[1].Weekly);
        Assert.Equal(40, snaps[1].Weekly!.Percent);
    }

    [Fact]
    public async Task Codex_ExitBeforeInitialize_IsAFailedStart()
    {
        // Three sessions that end before answering initialize must reach the fallback,
        // exactly like three processes that could not start at all.
        var starts = 0;
        var fallback = QuotaSnapshot.Unhealthy("codex", HealthState.Stale, Now, "file");
        var collector = new CodexCollector(
            () => { starts++; return new ScriptedProcess(); },
            () => Now, (_, _) => Task.CompletedTask, () => fallback);

        var snaps = await Take(collector, 1);

        Assert.Equal(HealthState.Stale, snaps[0].Health);
        Assert.True(starts >= 3, $"starts={starts}");
    }

    [Fact]
    public async Task Codex_RefreshIntervalComesFromTheSetting()
    {
        var delays = new List<TimeSpan>();
        var process = new ScriptedProcess(Init, FullRead);
        var collector = new CodexCollector(() => process, () => Now,
            (d, ct) => { delays.Add(d); return Task.Delay(Timeout.InfiniteTimeSpan, ct); },
            () => null, () => TimeSpan.FromSeconds(300));

        await Take(collector, 1);
        await Task.Delay(50);

        Assert.Contains(TimeSpan.FromSeconds(300), delays);
    }
}
