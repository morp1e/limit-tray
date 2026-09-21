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
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(60);
    /// <summary>How long the initialize handshake may take before the session counts as failed.</summary>
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<IJsonRpcProcess> _processFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<QuotaSnapshot?> _readFallback;
    private readonly Func<TimeSpan> _interval;
    private volatile IJsonRpcProcess? _active;
    private QuotaSnapshot? _lastFresh;

    public CodexCollector(
        Func<IJsonRpcProcess> processFactory,
        Func<DateTimeOffset> clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<QuotaSnapshot?> readFallback,
        Func<TimeSpan>? interval = null)
    {
        _processFactory = processFactory;
        _clock = clock;
        _delay = delay;
        _readFallback = readFallback;
        _interval = interval ?? (() => DefaultRefreshInterval);
    }

    public string Provider => CodexRateLimitsParser.Provider;

    public void RequestRefresh()
    {
        var process = _active;
        if (process is null) return;
        _ = process.SendAsync(ReadMessage, CancellationToken.None)
            .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

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
    /// <summary>
    /// Returns true only when the handshake completed: a process that could not be
    /// created, could not start, exited before answering initialize, or never answered
    /// within the timeout is a failed start, and three of those reach the fallback.
    /// </summary>
    private async Task<bool> RunSession(
        ChannelWriter<QuotaSnapshot> writer, CancellationToken ct)
    {
        IJsonRpcProcess process;
        try
        {
            // The factory itself can throw (codex.exe not found). That used to escape the
            // loop and stop Codex collection for the life of the process.
            process = _processFactory();
            await process.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return false;
        }

        using (process)
        {
            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(HandshakeTimeout);
            Task? refreshLoop = null;
            var initialized = false;

            try
            {
                await process.SendAsync(InitializeMessage, ct).ConfigureAwait(false);

                // Lines are read with the handshake token until initialize is answered, so
                // a process that stays alive but silent does not hang the session forever.
                await foreach (var line in process.ReadLines(handshakeCts.Token).ConfigureAwait(false))
                {
                    if (!IsInitializeResponse(line)) continue;
                    initialized = true;
                    break;
                }

                if (initialized)
                {
                    _active = process;
                    await process.SendAsync(InitializedNotification, ct).ConfigureAwait(false);
                    await process.SendAsync(ReadMessage, ct).ConfigureAwait(false);
                    refreshLoop = Task.Run(
                        () => RefreshRead(process, refreshCts.Token),
                        CancellationToken.None);

                    await foreach (var line in process.ReadLines(ct).ConfigureAwait(false))
                    {
                        if (!CarriesRateLimits(line)) continue;

                        var parsed = CodexRateLimitsParser.ParseAppServer(line, _clock());
                        await writer.WriteAsync(Merge(parsed), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (OperationCanceledException) { /* handshake timeout: a failed start */ }
            catch (Exception) { /* the process died; the loop restarts it */ }
            finally
            {
                _active = null;
                refreshCts.Cancel();
                if (refreshLoop is not null)
                {
                    // A faulted refresh loop (stdin closed under a write) must not escape
                    // here; the session is over either way and the loop decides what next.
                    try { await refreshLoop.ConfigureAwait(false); }
                    catch (Exception) { }
                }
            }

            return initialized;
        }
    }

    /// <summary>
    /// account/rateLimits/updated can carry only the window that changed. A partial
    /// notification must not erase the other window we already know; a fresh reading
    /// of one window is combined with the last fresh reading of the other.
    /// </summary>
    private QuotaSnapshot Merge(QuotaSnapshot parsed)
    {
        if (parsed.Health != HealthState.Fresh) return parsed;

        var previous = _lastFresh;
        var merged = parsed with
        {
            Session = parsed.Session ?? previous?.Session,
            Weekly = parsed.Weekly ?? previous?.Weekly,
        };
        _lastFresh = merged;
        return merged;
    }

    private async Task RefreshRead(IJsonRpcProcess process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _delay(_interval(), ct).ConfigureAwait(false);
                await process.SendAsync(ReadMessage, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private const string InitializeMessage = """
    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"limit-tray","title":"Lim'it","version":"0.3.4"}}}
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
