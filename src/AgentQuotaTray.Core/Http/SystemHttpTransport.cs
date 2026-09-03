namespace AgentQuotaTray.Core.Http;

public sealed class SystemHttpTransport : IHttpTransport, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<HttpTransportResult> GetAsync(
        string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new HttpTransportResult((int)response.StatusCode, body);
    }

    public void Dispose() => _client.Dispose();
}
