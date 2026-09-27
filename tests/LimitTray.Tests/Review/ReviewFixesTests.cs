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

    // Codex collector. The 2026-09-20 review pinned four behaviours of the long-lived
    // app-server session. v0.4 replaced that session with sparse one-shot reads
    // (docs/specs/2026-09-27-native-aot-design.md), and each rule moved with its code:
    //   partial update keeps the other window -> CodexCollectorTests.PartialReading_KeepsTheOtherWindow
    //   exit before initialize is a failed start -> CodexServerReaderTests.ReadOnce_ProcessEndsEarly_IsBroken
    //   refresh interval from the setting -> gone: Codex is read every 10 minutes, the setting is Claude's
    //   a throwing factory must not stop Codex collection for the life of the process -> below,
    //   end to end through the real reader, because that was the defect that froze the Codex card.

    [Fact]
    public async Task Codex_FactoryThrowing_DoesNotStopCollection()
    {
        var clock = Now;
        var reader = new CodexServerReader(
            () => throw new InvalidOperationException("codex.exe bulunamadi"), () => clock);
        var polls = 0;
        var collector = new CodexCollector(
            reader.ReadOnceAsync,
            () => ++polls == 3
                ? new QuotaSnapshot("codex", new QuotaWindow(12, null, TimeSpan.FromHours(5)), null,
                    HealthState.Fresh, clock, null)
                : null,
            () => clock,
            (delay, ct) => { clock += delay; return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        QuotaSnapshot? first = null;
        await foreach (var snapshot in collector.Watch(cts.Token))
        {
            first = snapshot;
            break;
        }

        // The reader failed on every attempt, yet the loop kept polling and delivered the file reading.
        Assert.NotNull(first);
        Assert.Equal(HealthState.Fresh, first!.Health);
        Assert.Equal(12, first.Session!.Percent);
    }
}
