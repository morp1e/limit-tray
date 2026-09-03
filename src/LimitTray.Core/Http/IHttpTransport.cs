namespace LimitTray.Core.Http;

public interface IHttpTransport
{
    Task<HttpTransportResult> GetAsync(
        string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
