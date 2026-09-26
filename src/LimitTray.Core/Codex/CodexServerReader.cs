using LimitTray.Core.Model;
using LimitTray.Core.Process;

namespace LimitTray.Core.Codex;

public sealed class CodexServerReader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<IJsonRpcProcess> _processFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _timeout;

    public CodexServerReader(
        Func<IJsonRpcProcess> processFactory,
        Func<DateTimeOffset> clock,
        TimeSpan? timeout = null)
    {
        _processFactory = processFactory;
        _clock = clock;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// Starts one app-server session, performs the initialize handshake and one rate-limit
    /// read, then disposes the process. Only caller cancellation escapes as cancellation.
    /// </summary>
    public async Task<QuotaSnapshot> ReadOnceAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        var exchangeToken = timeoutCts.Token;

        ct.ThrowIfCancellationRequested();

        IJsonRpcProcess process;
        try
        {
            process = _processFactory();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Broken("app-server baslatilamadi: " + ex.GetType().Name);
        }

        try
        {
            using (process)
            {
                try
                {
                    await process.StartAsync(exchangeToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The panel shows this text; a bare type name did not say what failed.
                    return Broken("app-server baslatilamadi: " + ex.GetType().Name);
                }

                await process.SendAsync(CodexProtocol.InitializeMessage, exchangeToken).ConfigureAwait(false);

                if (!await ReadResponse(process, 1, exchangeToken).ConfigureAwait(false))
                    return Broken("app-server erken kapandi");

                await process.SendAsync(CodexProtocol.InitializedNotification, exchangeToken)
                    .ConfigureAwait(false);
                await process.SendAsync(CodexProtocol.ReadMessage, exchangeToken).ConfigureAwait(false);

                await foreach (var line in process.ReadLines(exchangeToken).ConfigureAwait(false))
                {
                    if (CodexProtocol.IsResponse(line, 2))
                        return CodexRateLimitsParser.ParseAppServer(line, _clock());
                }

                return Broken("app-server erken kapandi");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return Broken("app-server zaman asimi");
        }
        catch (Exception ex)
        {
            return Broken("app-server hatasi: " + ex.GetType().Name);
        }
    }

    private static async Task<bool> ReadResponse(
        IJsonRpcProcess process, int id, CancellationToken ct)
    {
        await foreach (var line in process.ReadLines(ct).ConfigureAwait(false))
        {
            if (CodexProtocol.IsResponse(line, id)) return true;
        }

        return false;
    }

    private QuotaSnapshot Broken(string detail) =>
        QuotaSnapshot.Unhealthy(
            CodexRateLimitsParser.Provider,
            HealthState.ProtocolBroken,
            _clock(),
            detail);
}
