using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using LimitTray.Core.Collectors;
using LimitTray.Core.Model;
using LimitTray.Core.Process;

namespace LimitTray.Core.Codex;

public sealed class CodexCollector : IQuotaCollector
{
    private const int MaxStartAttempts = 3;
    private static readonly TimeSpan FirstRestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly Func<IJsonRpcProcess> _processFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<QuotaSnapshot?> _readFallback;

    public CodexCollector(
        Func<IJsonRpcProcess> processFactory,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<QuotaSnapshot?> readFallback)
    {
        _processFactory = processFactory;
        _clock = clock;
        _delay = delay;
        _readFallback = readFallback;
    }

    public string Provider => CodexRateLimitsParser.Provider;

    /// <summary>
    /// The session intentionally remains long-lived: account/rateLimits/updated
    /// notifications flow while the app-server remains connected. Therefore,
    /// snapshots are published as they arrive rather than in a batch at session
    /// end. A Channel is used because yield return cannot be used inside try/catch.
    /// </summary>
    public async IAsyncEnumerable<QuotaSnapshot> Watch(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<QuotaSnapshot>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        var pump = Task.Run(() => RunLoop(channel.Writer, ct), CancellationToken.None);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var snapshot))
                    yield return snapshot;
            }
        }
        finally
        {
            await pump.ConfigureAwait(false);
        }
    }

    private async Task RunLoop(ChannelWriter<QuotaSnapshot> writer, CancellationToken ct)
    {
        var failures = 0;
        var restartDelay = FirstRestartDelay;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var started = await RunSession(writer, ct).ConfigureAwait(false);

                if (started)
                {
                    failures = 0;
                    restartDelay = FirstRestartDelay;
                }
                else
                {
                    failures++;
                    if (failures >= MaxStartAttempts)
                    {
                        var fallback = _readFallback()
                            ?? QuotaSnapshot.Unhealthy(
                                Provider, HealthState.ProtocolBroken, _clock(),
                                "codex app-server baslatilamadi");
                        await writer.WriteAsync(fallback, ct).ConfigureAwait(false);
                        failures = 0;
                    }
                }

                await _delay(restartDelay, ct).ConfigureAwait(false);
                restartDelay = restartDelay + restartDelay > MaxRestartDelay
                    ? MaxRestartDelay
                    : restartDelay + restartDelay;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>Returns true if the process started and writes snapshots to the writer.</summary>
    private async Task<bool> RunSession(
        ChannelWriter<QuotaSnapshot> writer, CancellationToken ct)
    {
        using var process = _processFactory();

        try
        {
            await process.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return false;
        }

        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? refreshLoop = null;

        try
        {
            await process.SendAsync(InitializeMessage, ct).ConfigureAwait(false);

            var initialized = false;

            await foreach (var line in process.ReadLines(ct).ConfigureAwait(false))
            {
                if (!initialized && IsInitializeResponse(line))
                {
                    initialized = true;
                    await process.SendAsync(InitializedNotification, ct).ConfigureAwait(false);
                    await process.SendAsync(ReadMessage, ct).ConfigureAwait(false);
                    refreshLoop = Task.Run(
                        () => RefreshRead(process, refreshCts.Token),
                        CancellationToken.None);
                    continue;
                }

                if (!CarriesRateLimits(line)) continue;

                await writer.WriteAsync(
                    CodexRateLimitsParser.ParseAppServer(line, _clock()), ct)
                    .ConfigureAwait(false);
            }

        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* surec olduse yeniden baslatilir */ }
        finally
        {
            refreshCts.Cancel();
            if (refreshLoop is not null)
                await refreshLoop.ConfigureAwait(false);
        }

        return true;
    }

    private async Task RefreshRead(IJsonRpcProcess process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _delay(RefreshInterval, ct).ConfigureAwait(false);
                await process.SendAsync(ReadMessage, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private const string InitializeMessage = """
    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"limit-tray","title":"Lim'it","version":"0.1.0"}}}
    """;

    private const string InitializedNotification = """
    {"jsonrpc":"2.0","method":"initialized","params":{}}
    """;

    private const string ReadMessage = """
    {"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{}}
    """;

    private static bool IsInitializeResponse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1;
        }
        catch (JsonException) { return false; }
    }

    private static bool CarriesRateLimits(string line) =>
        line.Contains("\"rateLimits\"", StringComparison.Ordinal);
}
